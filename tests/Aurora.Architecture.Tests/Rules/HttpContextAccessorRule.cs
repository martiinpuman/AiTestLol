using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Aurora.Architecture.Tests.Metadata;

namespace Aurora.Architecture.Tests.Rules;

/// <summary>
/// Fitness rule <b>T4</b> - <c>IHttpContextAccessor</c> appears only in the tenant-resolution
/// middleware (ADR-0007 §3.3, <c>testing-strategy.md</c> §5.3).
/// </summary>
/// <remarks>
/// <para>
/// Blazor Server has no HTTP request during an interactive render. A component reading the tenant
/// from <c>IHttpContextAccessor</c> works in development, works on first render in production, and
/// then returns <c>null</c> or - worse - another circuit's stale value. The tenant is pinned to the
/// circuit instead, and this rule is what keeps the shortcut from creeping back.
/// </para>
/// <para>
/// <b>What the mechanism inspects:</b> every type mention in scope, from
/// <see cref="TypeReferences"/> - base types, interfaces, fields, properties, parameters, return
/// types, locals, and the declaring type, signature and generic arguments of every member an
/// instruction names. Injecting it, storing it, calling it or merely naming it in a local is
/// caught; matching is on the framework type's exact metadata name.
/// </para>
/// <para>
/// <b>What it cannot see:</b> resolution by name through a service locator
/// (<c>GetService(Type.GetType("..."))</c>), which is not expressible in IL as a reference to the
/// type.
/// </para>
/// </remarks>
internal static class HttpContextAccessorRule
{
    public const string Id = "T4";

    public const string Name = "IHttpContextAccessor only in the tenant-resolution middleware";

    public const string BannedType = "Microsoft.AspNetCore.Http.IHttpContextAccessor";

    /// <summary>
    /// The types allowed to name it: the tenant-resolution middleware, and nothing else.
    /// </summary>
    /// <remarks>
    /// Empty until B-06 and B-15 build the middleware. Empty is the *strict* state - the rule bans
    /// the type everywhere - so an inert allow-list cannot weaken the rule, and adding the
    /// middleware to it is a reviewable diff in this file. The exemption path itself is proven by
    /// a fixture test, so it is not an untested branch.
    /// </remarks>
    public static readonly ImmutableHashSet<string> AllowedTypes =
        ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);

    public static RuleOutcome Check(IEnumerable<ScannedType> types) => Check(types, AllowedTypes);

    public static RuleOutcome Check(IEnumerable<ScannedType> types, ImmutableHashSet<string> allowedTypes)
    {
        ScannedType[] subjects = [.. types.Where(type => !allowedTypes.Contains(type.FullName))];

        return RuleOutcome.From(
            Id,
            Name,
            "types",
            subjects.Length,
            subjects
                .SelectMany(TypeReferences.In)
                .Where(static reference => reference.Use.Mentions(BannedType))
                .Select(static reference => new RuleViolation(
                    reference.Subject,
                    reference.Site,
                    $"names {BannedType} as {reference.Context}")));
    }
}
