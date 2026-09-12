using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Aurora.Architecture.Tests.Metadata;

namespace Aurora.Architecture.Tests.Rules;

/// <summary>
/// A population of scanned types, indexed by full name, able to walk a base-type chain across
/// assembly boundaries.
/// </summary>
/// <remarks>
/// Cross-assembly is the point: <c>SalesDbContext</c> in one assembly derives from
/// <c>DbContext</c> in another, and a rule that gave up at the assembly boundary would never
/// recognise a tenant context at all - and would report no violations, in green. No compiled
/// fixture takes that hop, so <c>TypeIndexTests</c> takes it with
/// <c>Fixtures.CrossAssemblyFixture</c>: that is the test that goes red if the walk ever stops at
/// a boundary.
/// </remarks>
internal sealed class TypeIndex
{
    private readonly ImmutableDictionary<string, ScannedType> _byFullName;

    private TypeIndex(ImmutableDictionary<string, ScannedType> byFullName, ImmutableArray<ScannedType> all)
    {
        _byFullName = byFullName;
        All = all;
    }

    public ImmutableArray<ScannedType> All { get; }

    public static TypeIndex Of(IEnumerable<ScannedType> types)
    {
        ImmutableArray<ScannedType> all = [.. types];

        // A duplicate full name would make the base-type walk depend on enumeration order, so the
        // first wins and the behaviour is stated rather than accidental. Two assemblies declaring
        // the same full name is itself a smell, and one the L-rules would see as a reference.
        ImmutableDictionary<string, ScannedType> byFullName = all
            .GroupBy(static type => type.FullName, StringComparer.Ordinal)
            .ToImmutableDictionary(
                static group => group.Key,
                static group => group.First(),
                StringComparer.Ordinal);

        return new TypeIndex(byFullName, all);
    }

    public ScannedType? Find(string fullName) =>
        _byFullName.TryGetValue(fullName, out ScannedType? type) ? type : null;

    /// <summary>
    /// Does <paramref name="type"/> derive from <paramref name="baseTypeFullName"/>, however many
    /// steps away?
    /// </summary>
    /// <remarks>
    /// The walk stops at the first base type the population does not contain - typically a
    /// framework type - and compares names rather than identities, because nothing here is loaded.
    /// </remarks>
    public bool DerivesFrom(ScannedType type, string baseTypeFullName)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (ScannedType? current = type; current?.BaseTypeName is not null;)
        {
            if (string.Equals(current.BaseTypeName, baseTypeFullName, StringComparison.Ordinal))
            {
                return true;
            }

            if (!seen.Add(current.BaseTypeName))
            {
                return false;
            }

            current = Find(current.BaseTypeName);
        }

        return false;
    }

    /// <summary>
    /// Does <paramref name="type"/> or any base type within the population declare
    /// <paramref name="interfaceFullName"/> in its interface list?
    /// </summary>
    /// <remarks>
    /// Metadata lists an interface on the type that declares it, not on every type that inherits
    /// it, so a derived type implements an interface through a base it never names. A rule reading
    /// one interface list would miss the hosted service that inherits <c>IHostedService</c> from a
    /// base class - the same shape as T5's inherited-field case, and walked the same way.
    /// </remarks>
    public bool Implements(ScannedType type, string interfaceFullName) =>
        Chain(type).Any(current => current.InterfaceNames.Contains(interfaceFullName, StringComparer.Ordinal));

    /// <summary>Every field and property of a type and of its base types within the population.</summary>
    public IEnumerable<(string Owner, string Member, TypeUse Type, bool IsStatic)> MembersOf(ScannedType type)
    {
        foreach (ScannedType current in Chain(type))
        {
            foreach (ScannedField field in current.Fields)
            {
                yield return (current.FullName, field.Name, field.Type, field.IsStatic);
            }

            foreach (ScannedProperty property in current.Properties)
            {
                yield return (current.FullName, property.Name, property.Type, property.IsStatic);
            }
        }
    }

    /// <summary>A type and every base type of it the population contains, nearest first.</summary>
    private IEnumerable<ScannedType> Chain(ScannedType type)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (ScannedType? current = type; current is not null && seen.Add(current.FullName);)
        {
            yield return current;
            current = current.BaseTypeName is null ? null : Find(current.BaseTypeName);
        }
    }

    /// <summary>The last segment of a metadata full name: <c>Ns.Outer+Inner`1</c> becomes <c>Inner`1</c>.</summary>
    public static string SimpleNameOf(string fullName)
    {
        int separator = fullName.LastIndexOfAny(['.', '+']);
        return separator < 0 ? fullName : fullName[(separator + 1)..];
    }
}
