using System;
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
/// whose base-type chain reaches <c>Microsoft.EntityFrameworkCore.DbContext</c>, excluding the
/// catalog context as the exact (full name, assembly) pair <see cref="TenancyNames.CatalogContext"/>
/// - a look-alike at any other name or in any other assembly is a subject. <c>protected</c> and
/// <c>protected internal</c> count as reachable from outside; <c>private protected</c> does not,
/// because it is assembly-bounded like <c>internal</c>.
/// </para>
/// <para>
/// <b>What it cannot see:</b> a context handed out by a member other than its constructor - a
/// public factory method, a property, a field. ADR-0032 §4.2 assigns those to T10, T11 and T12,
/// which are not in this project yet; until they land, that door is closed by nothing here.
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
/// Fitness rule <b>T2</b> - the <c>AddDbContext</c> family is called only in
/// <c>Aurora.Platform.Tenancy</c>, and only for the catalog context (ADR-0007 §4.2 as amended by
/// ADR-0032 §4.1.1).
/// </summary>
/// <remarks>
/// <para>
/// Layer 2 of the structural guarantee. Nothing registers a tenant context, so
/// <c>GetRequiredService&lt;SalesDbContext&gt;()</c> throws at runtime rather than handing back a
/// context bound to no tenant - or worse, to the wrong one. The one conventional registration is
/// the catalog's (ADR-0003 rule 3), and it lives in the tenancy assembly.
/// </para>
/// <para>
/// <b>What the rule keys on:</b> the call site's assembly and the argument's full name - never
/// "is the argument a tenant context". The re-review (H-2) showed why: a helper
/// <c>AddTenantDbContext&lt;TContext&gt;()</c> forwards a type parameter, so a rule asking whether
/// the argument is a tenant context saw <c>!!0</c>, matched nothing, and still counted the call as
/// examined. Judged by its site, the helper's body is a violation wherever it lives.
/// </para>
/// <para>
/// <b>What the mechanism inspects:</b> every <c>call</c> in every production method body whose
/// member name begins <c>AddDbContext</c> (so <c>AddDbContextPool</c> and
/// <c>AddDbContextFactory</c> are included) or <c>AddPooledDbContextFactory</c>. That name selects
/// the <i>population</i>, which is the safe direction for a name to be load-bearing: a call that
/// dodges the name is not silently exempt here, it is outside this rule's population and inside
/// T9's (ADR-0032 §4.1.1), which keys on the container surface. A call is a violation when its
/// site is any assembly other than <c>Aurora.Platform.Tenancy</c>, whatever its arguments; or when,
/// inside that assembly, it has no generic argument or any generic argument other than exactly
/// <c>Aurora.Platform.Tenancy.Catalog.CatalogDbContext</c> - a type parameter the rule cannot
/// resolve included.
/// </para>
/// <para>
/// <b>What it cannot see:</b> a registration through any other container API -
/// <c>AddScoped&lt;SalesDbContext&gt;()</c>, a <c>ServiceDescriptor</c> built from a <c>Type</c>
/// value, the helper's <i>call site</i>. Those are T9's population, and until T9 lands they are
/// covered by nothing in this project.
/// </para>
/// </remarks>
internal static class TenantDbContextRegistrationRule
{
    public const string Id = "T2";

    public const string Name =
        "AddDbContext family called only inside Aurora.Platform.Tenancy, and only for the catalog context";

    /// <summary>
    /// The population key. A member name, deliberately: it selects what is counted, never what is
    /// exempt.
    /// </summary>
    public static bool IsDbContextRegistration(string memberName) =>
        memberName.StartsWith("AddDbContext", StringComparison.Ordinal)
        || memberName.StartsWith("AddPooledDbContextFactory", StringComparison.Ordinal);

    public static RuleOutcome Check(TypeIndex index)
    {
        (ScannedType Site, InstructionReference Call)[] registrations =
        [
            .. from site in index.All
               from call in TypeReferences.InstructionsIn(site)
               where IsDbContextRegistration(call.Member.MemberName)
               select (site, call),
        ];

        return RuleOutcome.From(
            Id,
            Name,
            "AddDbContext* calls",
            registrations.Length,
            registrations
                .Select(static registration => Judge(registration.Site, registration.Call))
                .Where(static violation => violation is not null)
                .Select(static violation => violation!));
    }

    private static RuleViolation? Judge(ScannedType site, InstructionReference call)
    {
        string arguments = string.Join(", ", call.Member.GenericArguments.Select(static argument => argument.Display));

        if (!string.Equals(site.AssemblyName, TenancyNames.TenancyAssemblyName, StringComparison.Ordinal))
        {
            return new RuleViolation(
                call.Subject,
                ViolationSite.MemberReference,
                $"{call.Member.MemberName}<{arguments}> is called from {site.AssemblyName}; the AddDbContext "
                + $"family is called only inside {TenancyNames.TenancyAssemblyName}, and only for the catalog "
                + "context (ADR-0032 §4.1.1)");
        }

        bool onlyTheCatalog =
            call.Member.GenericArguments.Length > 0
            && call.Member.GenericArguments.All(IsExactlyTheCatalog);

        return onlyTheCatalog
            ? null
            : new RuleViolation(
                call.Subject,
                ViolationSite.MemberReference,
                $"{call.Member.MemberName}<{arguments}> inside {TenancyNames.TenancyAssemblyName} registers "
                + $"something other than {TenancyNames.CatalogDbContext}; an argument the rule cannot resolve "
                + "is something other (ADR-0032 §4.1.1)");
    }

    /// <summary>A plain named type has exactly one name; a type parameter has none, an instantiation several.</summary>
    private static bool IsExactlyTheCatalog(TypeUse argument) =>
        argument.Names.Count == 1 && argument.Names.Contains(TenancyNames.CatalogDbContext);
}
