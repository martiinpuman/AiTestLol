using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Aurora.Platform.Tenancy.Contracts;

namespace Aurora.Platform.Tenancy.UnitTests.Kernel;

/// <summary>
/// What one scan found: how many public members and how many explicitly implemented interface
/// members it examined, which types it visited, and every mention of a proof.
/// </summary>
internal sealed record ProofMentionScanResult(int Examined, int ExplicitImplementations, SortedSet<string> VisitedTypes, List<string> Mentions);

/// <summary>What the derivability check found: every public type it examined, and every one that code outside the friend set could derive from, with why.</summary>
internal sealed record DerivabilityScanResult(SortedSet<string> Examined, List<string> Derivable);

/// <summary>
/// Link 3 of the construction chain, as two functions over the public types of the friend
/// assemblies. <see cref="Over"/> reports every public member, every interface member a type
/// implements explicitly, and every type, wherever a <see cref="TenantAccess"/> is mentioned - in
/// a member's signature (return type, any parameter, any generic argument, a generic constraint,
/// the <c>Invoke</c> signature of a delegate parameter, an event's handler type) or in what a type
/// inherits (its base chain and its interfaces). <see cref="DerivableTypes"/> reports every public
/// type that code outside the friend set could derive from, which is what closes the protected
/// members to it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why "mentions anywhere" rather than "hands out".</b> The first form of this scan read return
/// types and by-ref parameters and treated every other parameter as inbound. PR #13's first review
/// walked two shapes through it in green: an event (<c>add_</c>/<c>remove_</c> return <c>void</c>
/// and take a by-value parameter) and a callback parameter (<c>Action&lt;TenantScope&gt;</c> by
/// value), both of which hand a live scope to any assembly. Classifying direction is where the gaps
/// live, so this scan does not classify: any mention is reported, and the caller allow-lists the
/// inbound members and the sanctioned doors by exact key. A member missing from both fails loud.
/// </para>
/// <para>
/// <b>Why a type's inheritance counts.</b> The second review's door: <c>public sealed class
/// OpenScopeCollection : List&lt;TenantScope&gt;</c> declares no member, and a method returning it
/// names no proof, yet an assembly with no <c>[InternalsVisibleTo]</c> grant compiled a
/// <c>foreach (TenantScope scope in All())</c> against it. A public member can only expose a public
/// type of its own assembly, so a friend type that inherits a proof is itself the door; it is
/// reported once, naming everything in its base chain and interface set that mentions one. The
/// proof types themselves are reported this way too (<c>TenantScope : TenantAccess</c>) and are
/// allow-listed by name - the allow-list working as designed, so that a third proof type is a
/// visible entry, not a silent one.
/// </para>
/// <para>
/// <b>Why derivability counts.</b> The third review's door: <c>protected</c> and
/// <c>protected internal</c> members of a public type nobody sealed are reachable by anyone who
/// derives from it, and <c>BindingFlags.Public</c> never sees them - three protected doors added to
/// the contracts assembly left every test green, and an assembly with no grant compiled
/// <c>public TenantScope? Steal() =&gt; Held;</c> against them. Rather than widen the member scan to
/// protected members and decide, per type, whether they are reachable, <see cref="DerivableTypes"/>
/// asserts the simpler and stronger fact: no public type in the friend set can be derived from
/// outside it - each is sealed (a struct, an enum, a delegate, a static or sealed class), or has no
/// public or protected constructor (an abstract class with an internal one, which is
/// <c>TenantAccess</c>'s shape and the shape an EF migration takes once it is sealed by hand), and
/// no interface carries a protected member. With that true, no protected member is reachable from
/// outside the set - one of the two non-public routes closed; the next paragraph is the other.
/// </para>
/// <para>
/// <b>Why explicit interface implementations count.</b> The fourth review's door: a member
/// implemented explicitly is emitted private, so <see cref="EveryPublicMember"/> never sees it,
/// yet anyone holding the interface calls it; the type can be sealed, so the derivability check is
/// silent; and when the interface is non-generic and declared outside the scanned population, its
/// own type arguments name no proof, so the inheritance check is silent too. A public sealed type
/// in the tenancy assembly with one such member, implementing an interface from a third assembly,
/// left every test green while an assembly with no grant minted a live scope through it. So
/// <see cref="Over"/> follows the link the interface list only hints at: for every interface a
/// type implements, <see cref="Type.GetInterfaceMap"/> names the implementing methods whatever
/// their accessibility, and every non-public one declared on the type is examined and keyed like a
/// public member. A public implementation is already examined as a public member; an inherited one
/// is its declaring type's - examined there if that type is in the population, reported through
/// the inheritance check if it is a proof-bearing base outside it.
/// </para>
/// <para>
/// <b>The default arm throws.</b> A member kind this scan does not classify is a member kind it
/// cannot vouch for, and <c>_ =&gt; false</c> is exactly how the event went unseen. Constructors are
/// skipped on purpose (link 1 covers them) and nested types are expanded rather than classified.
/// </para>
/// <para>
/// <b>What this scan cannot see, stated so nobody over-trusts it.</b> It is a signature scan. A
/// member whose signature names no proof but whose value is one at runtime is invisible to it:
/// a return, field or parameter typed <c>object</c> or <c>dynamic</c>, a non-generic
/// <c>IEnumerable</c>, a base type or interface that does not itself carry the proof type.
/// So is a proof handed out inside something a signature does not describe - a serialised form
/// (link 4 covers the serialisers) - and every route reflection or <c>GetUninitializedObject</c>
/// takes (links 4 and 5). What the two functions together prove is this, and only this: in the
/// friend set, no public member's signature, no explicitly implemented interface member's
/// signature and no public type's inheritance names a proof except by allow-listed key, and no
/// public type can be derived from outside the set. Public members, explicit implementations and
/// protected members are the three ways a member is reached from outside without reflection, and
/// those three are what the scans read. Three reviews each found one of them unread; the honest
/// claim is that these three are covered now, not that the list is finished.
/// </para>
/// </remarks>
internal static class ProofMentionScan
{
    private const BindingFlags EveryPublicMember =
        BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    /// <summary>Declared or inherited: what a derived type reaches, not only what the type declares.</summary>
    private const BindingFlags EveryReachableMember =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

    /// <summary>The public types declared at the top level of an assembly; nested public types are reached by expansion.</summary>
    public static IEnumerable<Type> TopLevelPublicTypes(Assembly assembly) =>
        assembly.GetTypes().Where(static type => type.IsPublic);

    /// <summary>Scans the given types and, recursively, their nested public types, for mentions of a proof.</summary>
    public static ProofMentionScanResult Over(IEnumerable<Type> roots)
    {
        int examined = 0;
        int explicitImplementations = 0;
        SortedSet<string> visited = new(StringComparer.Ordinal);
        List<string> mentions = [];

        foreach (Type root in roots)
        {
            Visit(root);
        }

        return new ProofMentionScanResult(examined, explicitImplementations, visited, mentions);

        void Visit(Type type)
        {
            visited.Add(type.FullName!);

            string? inherited = InheritedProofMention(type);
            if (inherited is not null)
            {
                mentions.Add(inherited);
            }

            foreach (MemberInfo member in type.GetMembers(EveryPublicMember))
            {
                switch (member)
                {
                    case ConstructorInfo:
                        continue;
                    case Type nested:
                        Visit(nested);
                        continue;
                }

                examined++;
                if (SignatureMentionsProof(member))
                {
                    mentions.Add(MemberKey(type, member));
                }
            }

            foreach (MethodInfo implementation in ExplicitImplementations(type))
            {
                explicitImplementations++;
                if (SignatureMentionsProof(implementation))
                {
                    mentions.Add(MemberKey(type, implementation));
                }
            }
        }
    }

    /// <summary>
    /// The interface members <paramref name="type"/> implements explicitly - emitted private, so
    /// <see cref="EveryPublicMember"/> never sees them, yet called by anyone holding the interface.
    /// Only the ones declared on this type: an inherited one is its declaring type's. An interface
    /// has no map, and a public implementation is already examined as a public member.
    /// </summary>
    private static IEnumerable<MethodInfo> ExplicitImplementations(Type type) =>
        type.IsInterface
            ? []
            : type.GetInterfaces()
                .SelectMany(implemented => type.GetInterfaceMap(implemented).TargetMethods)
                .Where(target => target.DeclaringType == type && !target.IsPublic)
                .Distinct()
                .OrderBy(static target => target.Name, StringComparer.Ordinal);

    /// <summary>
    /// Examines the given types and, recursively, their nested public types, reporting every one
    /// that code outside its assembly could derive from - and therefore reach the protected members
    /// of - with the reason and the protected members it would reach.
    /// </summary>
    public static DerivabilityScanResult DerivableTypes(IEnumerable<Type> roots)
    {
        SortedSet<string> examined = new(StringComparer.Ordinal);
        List<string> derivable = [];

        foreach (Type root in roots)
        {
            Visit(root);
        }

        return new DerivabilityScanResult(examined, derivable);

        void Visit(Type type)
        {
            examined.Add(type.FullName!);

            string? how = HowDerivable(type);
            if (how is not null)
            {
                derivable.Add(type.FullName + ": " + how);
            }

            foreach (Type nested in type.GetNestedTypes(BindingFlags.Public))
            {
                Visit(nested);
            }
        }
    }

    /// <summary>
    /// The key a mention is reported and allow-listed under: the declaring type's full name, the
    /// member's name, and - for a method - its generic arity and parameter types, so that
    /// sanctioning one overload never sanctions its siblings. An indexer carries its index
    /// parameters the same way. Inherited mentions are keyed by <see cref="InheritedProofMention"/>.
    /// </summary>
    public static string MemberKey(Type type, MemberInfo member) => type.FullName + "." + member switch
    {
        MethodInfo method => method.Name + GenericArity(method) + "(" + Parameters(method.GetParameters()) + ")",
        PropertyInfo property when property.GetIndexParameters() is { Length: > 0 } index =>
            property.Name + "(" + Parameters(index) + ")",
        _ => member.Name,
    };

    /// <summary>
    /// The key for a type whose inheritance mentions a proof: its full name, a colon, and every
    /// mentioning base type and interface in ordinal order - one entry per type, so a type that
    /// gains or loses a proof-bearing interface changes its key and any allow-list entry for it
    /// goes stale visibly.
    /// </summary>
    public static string? InheritedProofMention(Type type)
    {
        string[] mentioning =
        [
            .. BaseChain(type).Concat(type.GetInterfaces())
                .Where(Mentions)
                .Select(static ancestor => ancestor.ToString())
                .Order(StringComparer.Ordinal),
        ];

        return mentioning.Length == 0 ? null : type.FullName + " : " + string.Join(", ", mentioning);
    }

    /// <summary>
    /// Why code outside the assembly could derive from <paramref name="type"/>, or
    /// <see langword="null"/> when it cannot. Structs, enums and delegates are sealed by
    /// definition; a static class is abstract and sealed; a class with no public or protected
    /// constructor cannot be derived from outside its assembly whatever else it declares; an
    /// interface can always be implemented, so it must carry no protected member.
    /// </summary>
    private static string? HowDerivable(Type type)
    {
        string[] protectedMembers = ProtectedMembers(type);

        if (type.IsInterface)
        {
            return protectedMembers.Length == 0
                ? null
                : "an interface with protected members, reachable by any implementation: " + string.Join(", ", protectedMembers);
        }

        if (!type.IsClass || type.IsSealed)
        {
            return null;
        }

        ConstructorInfo? reachable = type
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(static constructor => constructor.IsPublic || constructor.IsFamily || constructor.IsFamilyOrAssembly);

        return reachable is null
            ? null
            : $"derivable from outside through a {(reachable.IsPublic ? "public" : "protected")} constructor; "
              + "protected members it would expose: "
              + (protectedMembers.Length == 0 ? "none today" : string.Join(", ", protectedMembers));
    }

    /// <summary>
    /// The protected members a derived type would reach, by name, inherited ones included - a
    /// derivable type whose protected doors are all its base's would otherwise read as exposing
    /// "none today" (fourth review). Constructors are the door itself and are named in the
    /// verdict's first clause, so they are not listed here again; <see cref="object"/>'s own
    /// <c>Finalize</c> and <c>MemberwiseClone</c> are every type's and name nothing.
    /// </summary>
    private static string[] ProtectedMembers(Type type) =>
    [
        .. type.GetMembers(EveryReachableMember)
            .Where(IsProtected)
            .Where(static member => member.DeclaringType != typeof(object))
            .Select(static member => member.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal),
    ];

    private static bool IsProtected(MemberInfo member) => member switch
    {
        ConstructorInfo => false,
        MethodBase method => method.IsFamily || method.IsFamilyOrAssembly,
        FieldInfo field => field.IsFamily || field.IsFamilyOrAssembly,
        PropertyInfo property => property.GetAccessors(nonPublic: true).Any(static accessor => accessor.IsFamily || accessor.IsFamilyOrAssembly),
        EventInfo @event => new[] { @event.AddMethod, @event.RemoveMethod }
            .Any(static accessor => accessor is { } method && (method.IsFamily || method.IsFamilyOrAssembly)),
        Type nested => nested.IsNestedFamily || nested.IsNestedFamORAssem,
        _ => throw new NotSupportedException(
            $"{member.MemberType} '{member.Name}' is a member kind the derivability check does not classify. Classify it."),
    };

    private static IEnumerable<Type> BaseChain(Type type)
    {
        for (Type? ancestor = type.BaseType; ancestor is not null; ancestor = ancestor.BaseType)
        {
            yield return ancestor;
        }
    }

    private static string GenericArity(MethodInfo method) =>
        method.IsGenericMethodDefinition
            ? "<" + string.Join(", ", method.GetGenericArguments().Select(static argument => argument.Name)) + ">"
            : string.Empty;

    private static string Parameters(ParameterInfo[] parameters) =>
        string.Join(", ", parameters.Select(static parameter => parameter.ParameterType.ToString()));

    private static bool SignatureMentionsProof(MemberInfo member) => member switch
    {
        MethodInfo method => Mentions(method.ReturnType)
            || method.GetParameters().Any(static parameter => Mentions(parameter.ParameterType))
            || (method.IsGenericMethodDefinition && method.GetGenericArguments().Any(Mentions)),
        PropertyInfo property => Mentions(property.PropertyType)
            || property.GetIndexParameters().Any(static parameter => Mentions(parameter.ParameterType)),
        FieldInfo field => Mentions(field.FieldType),
        EventInfo @event => Mentions(@event.EventHandlerType!),
        _ => throw new NotSupportedException(
            $"{member.MemberType} '{member.Name}' is a member kind this scan does not classify. Classify it; " +
            "a kind that falls through unclassified is a door nobody is reading."),
    };

    private static bool Mentions(Type type) => Mentions(type, []);

    private static bool Mentions(Type type, HashSet<Type> visiting)
    {
        if (!visiting.Add(type))
        {
            return false;
        }

        if (type.HasElementType)
        {
            return Mentions(type.GetElementType()!, visiting);
        }

        if (typeof(TenantAccess).IsAssignableFrom(type))
        {
            return true;
        }

        if (type.IsGenericParameter)
        {
            return type.GetGenericParameterConstraints().Any(constraint => Mentions(constraint, visiting));
        }

        if (type.IsGenericType && type.GetGenericArguments().Any(argument => Mentions(argument, visiting)))
        {
            return true;
        }

        if (typeof(Delegate).IsAssignableFrom(type))
        {
            MethodInfo? invoke = type.GetMethod("Invoke");
            return invoke is not null
                && (Mentions(invoke.ReturnType, visiting)
                    || invoke.GetParameters().Any(parameter => Mentions(parameter.ParameterType, visiting)));
        }

        return false;
    }
}
