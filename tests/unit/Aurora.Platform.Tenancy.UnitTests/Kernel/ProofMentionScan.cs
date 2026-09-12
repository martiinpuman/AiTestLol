using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Aurora.Platform.Tenancy.Contracts;

namespace Aurora.Platform.Tenancy.UnitTests.Kernel;

/// <summary>What one scan found: how many members it examined, which types it visited, and every mention of a proof.</summary>
internal sealed record ProofMentionScanResult(int Examined, SortedSet<string> VisitedTypes, List<string> Mentions);

/// <summary>
/// Link 3 of the construction chain, as a function: every public type it is given, their nested
/// public types, and every public member of each, reported wherever a <see cref="TenantAccess"/>
/// is mentioned - in a member's signature (return type, any parameter, any generic argument, a
/// generic constraint, the <c>Invoke</c> signature of a delegate parameter, an event's handler
/// type) or in what a type inherits (its base chain and its interfaces).
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
/// takes (links 4 and 5). What it proves is exactly this: no public signature and no public type's
/// inheritance in the friend set admits a proof except by name.
/// </para>
/// </remarks>
internal static class ProofMentionScan
{
    private const BindingFlags EveryPublicMember =
        BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    /// <summary>The public types declared at the top level of an assembly; nested public types are reached by expansion.</summary>
    public static IEnumerable<Type> TopLevelPublicTypes(Assembly assembly) =>
        assembly.GetTypes().Where(static type => type.IsPublic);

    /// <summary>Scans the given types and, recursively, their nested public types.</summary>
    public static ProofMentionScanResult Over(IEnumerable<Type> roots)
    {
        int examined = 0;
        SortedSet<string> visited = new(StringComparer.Ordinal);
        List<string> mentions = [];

        foreach (Type root in roots)
        {
            Visit(root);
        }

        return new ProofMentionScanResult(examined, visited, mentions);

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
