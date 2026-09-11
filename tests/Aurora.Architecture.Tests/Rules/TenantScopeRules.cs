using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Aurora.Architecture.Tests.Metadata;

namespace Aurora.Architecture.Tests.Rules;

/// <summary>
/// Fitness rule <b>T3</b> - only <c>Aurora.Platform.Tenancy</c> implements
/// <c>ITenantDbContextFactory&lt;&gt;</c> (ADR-0007 §4.2, §12.3).
/// </summary>
/// <remarks>
/// <para>
/// The factory is the single door to a tenant <c>DbContext</c>, and it is the code that applies the
/// connection-identity check of ADR-0007 §4.3. A second implementation somewhere else is a second
/// door, and nothing would make it apply the check.
/// </para>
/// <para>
/// <b>What the mechanism inspects:</b> the implemented-interface list of every production type,
/// matched on the simple name <c>ITenantDbContextFactory`1</c> so the rule is live the moment B-06
/// declares the interface, in whichever namespace it chooses.
/// </para>
/// <para>
/// <b>What it cannot see:</b> an implementation in a <b>test</b> assembly, because the population
/// is the projects under <c>src/</c>. That is deliberate - ADR-0007 §3.4 grants the tenancy test
/// assembly <c>InternalsVisibleTo</c>, so a test double there is expected - and it is a stated
/// scope, not an accident.
/// </para>
/// </remarks>
internal static class TenantDbContextFactoryRule
{
    public const string Id = "T3";

    public const string Name = "ITenantDbContextFactory<> implemented only in Aurora.Platform.Tenancy";

    public static RuleOutcome Check(IEnumerable<ScannedType> types)
    {
        ScannedType[] subjects = [.. types];

        return RuleOutcome.From(
            Id,
            Name,
            "types",
            subjects.Length,
            from type in subjects
            where !type.AssemblyName.StartsWith(TenancyNames.TenancyAssemblyPrefix, StringComparison.Ordinal)
            from implemented in type.InterfaceNames
            where string.Equals(
                TypeIndex.SimpleNameOf(implemented),
                TenancyNames.TenantDbContextFactorySimpleName,
                StringComparison.Ordinal)
            select new RuleViolation(
                type.FullName,
                ViolationSite.TypeShape,
                $"implements {implemented} outside {TenancyNames.TenancyAssemblyPrefix}; the factory is the "
                + "only door to a tenant DbContext and the only place the ADR-0007 §4.3 identity check runs"));
    }
}

/// <summary>
/// Fitness rule <b>T5</b> - nothing that outlives a single unit of work holds a
/// <c>TenantScope</c> (ADR-0007 §10.4, §12.3).
/// </summary>
/// <remarks>
/// <para>
/// A <c>TenantScope</c> is valid for one unit of work and <c>IsActive</c> goes false when it is
/// disposed. A singleton holding one serves tenant A's scope to tenant B's request - the precise
/// cross-tenant leak database-per-tenant exists to make impossible, reintroduced above the
/// database.
/// </para>
/// <para>
/// <b>What the mechanism inspects, in two halves:</b>
/// </para>
/// <list type="number">
///   <item><description>
///     <b>Static holders.</b> Any <c>static</c> field or property in production code whose type
///     names a <c>TenantScope</c>. A static is a singleton whether or not a container knows about
///     it, and this half needs no registration call to find it.
///   </description></item>
///   <item><description>
///     <b>Container singletons.</b> Every generic call whose member name contains
///     <c>Singleton</c> - <c>AddSingleton</c>, <c>TryAddSingleton</c>, <c>AddKeyedSingleton</c>,
///     <c>ServiceDescriptor.Singleton</c> - contributes its generic arguments as singleton types;
///     each is then checked, through its base-type chain, for a field or property naming a
///     <c>TenantScope</c>.
///   </description></item>
/// </list>
/// <para>
/// <b>What it cannot see:</b> a registration whose service and implementation types are
/// <c>Type</c> values rather than generic arguments (<c>new ServiceDescriptor(typeof(X),
/// typeof(Y), ServiceLifetime.Singleton)</c>), and a scope reached through a captured closure
/// rather than a declared member. The name of this rule says "field or property", not "held", for
/// that reason.
/// </para>
/// </remarks>
internal static class TenantScopeSingletonRule
{
    public const string Id = "T5";

    public const string Name = "No static or singleton-registered type has a TenantScope field or property";

    public static bool IsSingletonRegistration(string memberName) =>
        memberName.Contains("Singleton", StringComparison.Ordinal);

    /// <summary>Every type named as a generic argument of a singleton registration.</summary>
    public static ImmutableHashSet<string> SingletonRegisteredTypeNames(IEnumerable<ScannedType> types) =>
    [
        .. types
            .SelectMany(TypeReferences.InstructionsIn)
            .Where(static reference => IsSingletonRegistration(reference.Member.MemberName))
            .SelectMany(static reference => reference.Member.GenericArguments)
            .SelectMany(static argument => argument.Names),
    ];

    public static RuleOutcome Check(TypeIndex index)
    {
        ImmutableHashSet<string> singletons = SingletonRegisteredTypeNames(index.All);

        return RuleOutcome.From(
            Id,
            Name,
            "types, plus the types named by singleton registrations",
            index.All.Length,
            [.. StaticHolders(index), .. SingletonHolders(index, singletons)]);
    }

    private static IEnumerable<RuleViolation> StaticHolders(TypeIndex index) =>
        from type in index.All
        from reference in TypeReferences.In(type)
        where reference.Site is ViolationSite.Field or ViolationSite.Property
            && reference.Context.StartsWith("static", StringComparison.Ordinal)
            && TenancyNames.MentionsTenantScope(reference.Use)
        select new RuleViolation(
            reference.Subject,
            reference.Site,
            $"{reference.Context} of type {reference.Use.Display}; a static outlives every unit of work, "
            + "so the scope it holds is served to the wrong tenant (ADR-0007 §10.4)");

    private static IEnumerable<RuleViolation> SingletonHolders(TypeIndex index, ImmutableHashSet<string> singletons) =>
        from registered in singletons
        let type = index.Find(registered)
        where type is not null
        from member in index.MembersOf(type)
        where TenancyNames.MentionsTenantScope(member.Type)
        select new RuleViolation(
            $"{member.Owner}.{member.Member}",
            ViolationSite.Field,
            $"{registered} is registered as a singleton and holds a TenantScope in {member.Type.Display}; "
            + "a singleton serves one tenant's scope to another tenant's request (ADR-0007 §10.4)");
}
