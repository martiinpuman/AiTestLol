using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Aurora.Architecture.Tests.Metadata;

namespace Aurora.Architecture.Tests.Rules;

/// <summary>
/// Fitness rule <b>T3</b> - only <c>Aurora.Platform.Tenancy</c> implements a tenant
/// <c>DbContext</c> factory (ADR-0007 §4.2, §12.3; ADR-0027 §1).
/// </summary>
/// <remarks>
/// <para>
/// The factory is the single door to a tenant <c>DbContext</c>, and it is the code that applies the
/// connection-identity check of ADR-0007 §4.3. A second implementation somewhere else is a second
/// door, and nothing would make it apply the check.
/// </para>
/// <para>
/// <b>What the mechanism inspects:</b> the implemented-interface list of every production type
/// whose assembly is not <c>Aurora.Platform.Tenancy</c> or a dotted segment below it
/// (<see cref="TenancyNames.IsWithin"/>), matched on the simple names
/// <c>ITenantDbContextFactory`1</c> and ADR-0027's DDL-path sibling
/// <c>ITenantMigrationContextFactory`1</c> - the ADR says "the same fitness rule ... applies to it" -
/// so the rule is live the moment B-06 declares either, in whichever namespace it chooses.
/// </para>
/// <para>
/// <b>The allow-list is an interim form, pending ADR-0032 §4.4.</b> A plain prefix let
/// <c>Aurora.Platform.TenancyBypass</c> authorise itself (re-review m-2, executed); the
/// segment-bounded form refuses that and admits <c>Aurora.Platform.Tenancy.Contracts</c>, which is
/// where the factory interfaces are declared and which the ADR's first exact list wrongly omitted.
/// It is a floor, not the decision: any <c>Aurora.Platform.Tenancy.Anything</c> still authorises
/// itself, and only the exact set the ADR will settle closes that.
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

    public const string Name =
        "ITenantDbContextFactory<> and ITenantMigrationContextFactory<> implemented only in Aurora.Platform.Tenancy";

    public static RuleOutcome Check(IEnumerable<ScannedType> types)
    {
        ScannedType[] subjects = [.. types];

        return RuleOutcome.From(
            Id,
            Name,
            "types",
            subjects.Length,
            from type in subjects
            where !TenancyNames.IsWithin(type.AssemblyName, TenancyNames.TenancyAssemblyPrefix)
            from implemented in type.InterfaceNames
            where TenancyNames.TenantContextFactorySimpleNames.Contains(TypeIndex.SimpleNameOf(implemented))
            select new RuleViolation(
                type.FullName,
                ViolationSite.TypeShape,
                $"implements {implemented} outside {TenancyNames.TenancyAssemblyPrefix}; the factory is the "
                + "only door to a tenant DbContext and the only place the ADR-0007 §4.3 identity check runs"));
    }
}

/// <summary>
/// Fitness rule <b>T5</b> - nothing that outlives a single unit of work holds a tenant access
/// proof (ADR-0007 §10.4, §12.3; ADR-0027 §1).
/// </summary>
/// <remarks>
/// <para>
/// A <c>TenantScope</c> is valid for one unit of work and <c>IsActive</c> goes false when it is
/// disposed. A singleton holding one serves tenant A's scope to tenant B's request - the precise
/// cross-tenant leak database-per-tenant exists to make impossible, reintroduced above the
/// database.
/// </para>
/// <para>
/// <b>What the mechanism inspects, in three parts:</b>
/// </para>
/// <list type="number">
///   <item><description>
///     <b>Static holders.</b> Any <c>static</c> field or property in production code whose type
///     names a <c>TenantScope</c>, a <c>TenantDatabaseHandle</c> or their <c>TenantAccess</c> base.
///     A static is a singleton whether or not a container knows about it, and this part needs no
///     registration call to find it. The base type is in the set deliberately: a field typed
///     <c>TenantAccess</c> holds one tenant's proof exactly as the derived types do.
///   </description></item>
///   <item><description>
///     <b>Container singletons.</b> Every generic call whose member name contains
///     <c>Singleton</c> - <c>AddSingleton</c>, <c>TryAddSingleton</c>, <c>AddKeyedSingleton</c>,
///     <c>ServiceDescriptor.Singleton</c> - contributes its generic arguments as singleton types.
///   </description></item>
///   <item><description>
///     <b>Hosted services, by shape.</b> Every type whose base chain reaches
///     <c>Microsoft.Extensions.Hosting.BackgroundService</c>, or whose interface list - its own or
///     a base type's - names <c>Microsoft.Extensions.Hosting.IHostedService</c>. A hosted service is
///     a container singleton however its registration is spelled, and <c>AddHostedService</c>
///     contains no "Singleton", so the re-review's background worker (H-3) was never in the second
///     set. This part keys on the exact framework names rather than on a member name, because a
///     type cannot be hosted without them (ADR-0032 §2); B-07's saga and B-08's runner are this
///     shape and hold a <c>TenantDatabaseHandle</c> for their whole life.
///   </description></item>
/// </list>
/// <para>
/// Each type in the second and third sets is checked, through its base-type chain, for a field or
/// property naming any of the three proof types.
/// </para>
/// <para>
/// <b>What it cannot see:</b> a registration whose service and implementation types are
/// <c>Type</c> values rather than generic arguments (<c>new ServiceDescriptor(typeof(X),
/// typeof(Y), ServiceLifetime.Singleton)</c>); a singleton whose implementation appears only inside
/// a factory lambda (<c>AddSingleton&lt;IFoo&gt;(sp =&gt; new Impl())</c> names <c>IFoo</c> as its
/// generic argument and <c>Impl</c> only in a compiler-generated closure); and a scope reached
/// through a captured closure rather than a declared member. The name of this rule says "field or
/// property", not "held", for that reason.
/// </para>
/// </remarks>
internal static class TenantScopeSingletonRule
{
    public const string Id = "T5";

    public const string Name =
        "No static, singleton-registered or hosted type has a TenantScope, TenantDatabaseHandle or TenantAccess field or property";

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

    /// <summary>Every type that is a hosted service by shape, whatever its registration is called.</summary>
    public static ImmutableHashSet<string> HostedServiceTypeNames(TypeIndex index) =>
    [
        .. index.All
            .Where(type =>
                index.DerivesFrom(type, TenancyNames.BackgroundService)
                || index.Implements(type, TenancyNames.HostedService))
            .Select(static type => type.FullName),
    ];

    /// <summary>Every type with singleton lifetime: registered as one, or hosted.</summary>
    public static ImmutableHashSet<string> SingletonLifetimeTypeNames(TypeIndex index) =>
        SingletonRegisteredTypeNames(index.All).Union(HostedServiceTypeNames(index));

    public static RuleOutcome Check(TypeIndex index)
    {
        ImmutableHashSet<string> singletons = SingletonLifetimeTypeNames(index);

        // The count is every type; the kind says how many of them the singleton half actually
        // examined, so "no violations" cannot hide a singleton set that collapsed to zero.
        return RuleOutcome.From(
            Id,
            Name,
            FormattableString.Invariant($"types, of which {singletons.Count} have singleton lifetime"),
            index.All.Length,
            [.. StaticHolders(index), .. SingletonHolders(index, singletons)]);
    }

    private static IEnumerable<RuleViolation> StaticHolders(TypeIndex index) =>
        from type in index.All
        from reference in TypeReferences.In(type)
        where reference.Site is ViolationSite.Field or ViolationSite.Property
            && reference.Context.StartsWith("static", StringComparison.Ordinal)
            && TenancyNames.MentionsTenantAccess(reference.Use)
        select new RuleViolation(
            reference.Subject,
            reference.Site,
            $"{reference.Context} of type {reference.Use.Display}; a static outlives every unit of work, "
            + "so the tenant proof it holds is served to the wrong tenant (ADR-0007 §10.4)");

    private static IEnumerable<RuleViolation> SingletonHolders(TypeIndex index, ImmutableHashSet<string> singletons) =>
        from registered in singletons
        let type = index.Find(registered)
        where type is not null
        from member in index.MembersOf(type)
        where TenancyNames.MentionsTenantAccess(member.Type)
        select new RuleViolation(
            $"{member.Owner}.{member.Member}",
            ViolationSite.Field,
            $"{registered} has singleton lifetime (registered as one, or hosted) and holds a tenant proof in "
            + $"{member.Type.Display}; a singleton serves one tenant's proof to another tenant's request "
            + "(ADR-0007 §10.4)");
}
