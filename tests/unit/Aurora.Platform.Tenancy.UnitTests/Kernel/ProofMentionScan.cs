using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Aurora.Platform.Tenancy.Contracts;

namespace Aurora.Platform.Tenancy.UnitTests.Kernel;

/// <summary>What one scan found: how many members it examined, which types it visited, and every member whose signature mentions a proof.</summary>
internal sealed record ProofMentionScanResult(int Examined, SortedSet<string> VisitedTypes, List<string> Mentions);

/// <summary>
/// Link 3 of the construction chain, as a function: every public member of every public type it is
/// given, and of their nested public types, whose signature mentions a <see cref="TenantAccess"/>
/// anywhere - return type, any parameter, any generic argument, a generic constraint, the
/// <c>Invoke</c> signature of a delegate parameter, an event's handler type.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why "mentions anywhere" rather than "hands out".</b> The first form of this scan read return
/// types and by-ref parameters and treated every other parameter as inbound. PR #13's security
/// review walked two shapes through it in green: an event (<c>add_</c>/<c>remove_</c> return
/// <c>void</c> and take a by-value parameter) and a callback parameter (<c>Action&lt;TenantScope&gt;</c>
/// by value), both of which hand a live scope to any assembly. Classifying direction is where the
/// gaps live, so this scan does not classify: any mention is reported, and the caller allow-lists the
/// inbound members by name. The allow-list is short - ADR-0007 §4.5's handlers take a scope by
/// value, and the factory interfaces are the only ones in the friend assemblies - and a member
/// missing from it fails loud, not silent.
/// </para>
/// <para>
/// <b>The default arm throws.</b> A member kind this scan does not classify is a member kind it
/// cannot vouch for, and <c>_ =&gt; false</c> is exactly how the event went unseen. Constructors are
/// skipped on purpose (link 1 covers them) and nested types are expanded rather than classified.
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
                    mentions.Add(type.FullName + "." + member.Name);
                }
            }
        }
    }

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
