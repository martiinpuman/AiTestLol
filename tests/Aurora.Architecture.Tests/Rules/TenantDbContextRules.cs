using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Aurora.Architecture.Tests.Metadata;

namespace Aurora.Architecture.Tests.Rules;

/// <summary>
/// Fitness rule <b>T1</b> - no tenant <c>DbContext</c> has a public or protected constructor
/// (ADR-0007 §4.1, §12.3).
/// </summary>
/// <remarks>
/// <para>
/// Layer 1 of the structural guarantee. A tenant context has exactly one constructor and it is
/// <c>internal</c>, taking a <c>TenantScope</c>; the only public way to obtain one is
/// <c>ITenantDbContextFactory&lt;&gt;.CreateAsync(scope, ct)</c>, and a scope cannot be
/// manufactured. A public constructor reopens the door this design closed.
/// </para>
/// <para>
/// <b>What the mechanism inspects:</b> the accessibility flag on every <c>.ctor</c> of every type
/// whose base-type chain reaches <c>Microsoft.EntityFrameworkCore.DbContext</c>, excluding
/// <c>CatalogDbContext</c>. <c>protected</c> and <c>protected internal</c> count as reachable from
/// outside; <c>private protected</c> does not, because it is assembly-bounded like <c>internal</c>.
/// </para>
/// <para>
/// <b>What it cannot see:</b> a context reachable through a public factory method that is not the
/// tenant factory. That is T3's job, not this rule's.
/// </para>
/// </remarks>
internal static class TenantDbContextConstructorRule
{
    public const string Id = "T1";

    public const string Name = "No public or protected constructor on a tenant DbContext";

    public static RuleOutcome Check(TypeIndex index)
    {
        ImmutableArray<ScannedType> contexts = TenancyNames.TenantContextsIn(index);

        return RuleOutcome.From(
            Id,
            Name,
            "tenant DbContext types",
            contexts.Length,
            from context in contexts
            from constructor in context.Methods
            where constructor.IsConstructor && constructor.IsPublicOrProtected
            select new RuleViolation(
                $"{context.FullName}..ctor",
                ViolationSite.Signature,
                $"constructor is {constructor.Accessibility}; a tenant DbContext is constructed only by "
                + "ITenantDbContextFactory<> inside Aurora.Platform.Tenancy (ADR-0007 §4.1)"));
    }
}

/// <summary>
/// Fitness rule <b>T2</b> - no tenant <c>DbContext</c> is registered in the DI container
/// (ADR-0007 §4.2, §12.3).
/// </summary>
/// <remarks>
/// <para>
/// Layer 2 of the structural guarantee. Nothing registers a tenant context, so
/// <c>GetRequiredService&lt;SalesDbContext&gt;()</c> throws at runtime rather than handing back a
/// context bound to no tenant - or worse, to the wrong one.
/// </para>
/// <para>
/// <b>What the mechanism inspects:</b> every <c>call</c> in every production method body whose
/// member name begins <c>AddDbContext</c> (so <c>AddDbContextPool</c> and
/// <c>AddDbContextFactory</c> are included) or <c>AddPooledDbContextFactory</c>, and whose generic
/// arguments name a tenant context.
/// </para>
/// <para>
/// <b>What it cannot see:</b> a registration that names the context type only at runtime - a
/// non-generic <c>ServiceDescriptor</c> built from a <c>Type</c> value, or a factory lambda
/// returning <c>new SalesDbContext(...)</c> registered against <c>object</c>. The second is caught
/// by T1 instead, since constructing one needs an accessible constructor.
/// </para>
/// </remarks>
internal static class TenantDbContextRegistrationRule
{
    public const string Id = "T2";

    public const string Name = "No AddDbContext registration of a tenant DbContext";

    public static bool IsDbContextRegistration(string memberName) =>
        memberName.StartsWith("AddDbContext", StringComparison.Ordinal)
        || memberName.StartsWith("AddPooledDbContextFactory", StringComparison.Ordinal);

    public static RuleOutcome Check(TypeIndex index)
    {
        ImmutableHashSet<string> tenantContexts =
        [
            .. TenancyNames.TenantContextsIn(index).Select(static context => context.FullName),
        ];

        InstructionReference[] registrations =
        [
            .. index.All
                .SelectMany(TypeReferences.InstructionsIn)
                .Where(static reference => IsDbContextRegistration(reference.Member.MemberName)),
        ];

        return RuleOutcome.From(
            Id,
            Name,
            "AddDbContext* calls",
            registrations.Length,
            Violations(registrations, tenantContexts));
    }

    private static IEnumerable<RuleViolation> Violations(
        IEnumerable<InstructionReference> registrations,
        ImmutableHashSet<string> tenantContexts) =>
        from registration in registrations
        let named = registration.Member.GenericArguments
            .SelectMany(static argument => argument.Names)
            .Where(tenantContexts.Contains)
            .ToArray()
        where named.Length > 0
        select new RuleViolation(
            registration.Subject,
            ViolationSite.MemberReference,
            $"{registration.Member.MemberName} registers the tenant context(s) {string.Join(", ", named)}; "
            + "only CatalogDbContext is registered conventionally (ADR-0007 §4.2)");
}
