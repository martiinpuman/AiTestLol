using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Aurora.Architecture.Tests.Rules;

/// <summary>
/// The allowed dependency matrix of <c>modules.md</c> §6, as data.
/// </summary>
/// <remarks>
/// <para>
/// One row per consumer module, holding the providers it may hold a <b>compile-time reference</b>
/// to. The matrix's <c>E</c> cells - "events only, no reference" - are absent here, because that is
/// exactly what they mean: Ledger may react to a <c>Parties</c> event but may not reference the
/// module, so <c>Parties</c> does not appear in Ledger's row.
/// </para>
/// <para>
/// The point of holding it as data is procedural. Adding a reference means editing this table,
/// which means a reviewer sees the edit next to the project file that needed it. A rule that
/// derived the matrix from the tiers alone would silently permit every same-tier and downward
/// reference, including the three <c>modules.md</c> calls out as forbidden: Ledger to Parties,
/// Payments to Products, and the peer references between Sales, Purchasing and Payments.
/// </para>
/// </remarks>
internal static class ModuleMatrix
{
    /// <summary>Consumer module to the provider modules it may reference. Keys are the module names in project names.</summary>
    public static readonly ImmutableDictionary<string, ImmutableHashSet<string>> AllowedProviders =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Organization"] = [],
            ["Parties"] = ["Organization"],
            ["Products"] = ["Organization"],
            ["Ledger"] = ["Organization"],
            ["Tax"] = ["Organization", "Parties", "Products"],
            ["Inventory"] = ["Organization", "Parties", "Products", "Ledger", "Tax"],
            ["Sales"] = ["Organization", "Parties", "Products", "Ledger", "Tax", "Inventory"],
            ["Purchasing"] = ["Organization", "Parties", "Products", "Ledger", "Tax", "Inventory"],
            ["Payments"] = ["Organization", "Parties", "Ledger"],
            ["DocumentExchange"] = ["Organization", "Parties"],
            ["Reporting"] =
                ["Organization", "Parties", "Products", "Ledger", "Tax", "Inventory"],
        }.ToImmutableDictionary(
            static row => row.Key,
            static row => ImmutableHashSet.Create(StringComparer.Ordinal, row.Value),
            StringComparer.Ordinal);

    /// <summary>May <paramref name="consumer"/> hold a compile-time reference to <paramref name="provider"/>?</summary>
    /// <remarks>
    /// An unknown consumer is <b>not</b> permissive: a module missing from the matrix may reference
    /// no other module at all, so adding a module without adding its row fails the rule rather than
    /// exempting it. The opposite default is how a matrix quietly stops covering half the solution.
    /// </remarks>
    public static bool Allows(string consumer, string provider) =>
        string.Equals(consumer, provider, StringComparison.Ordinal)
        || (AllowedProviders.TryGetValue(consumer, out ImmutableHashSet<string>? providers)
            && providers.Contains(provider));

    /// <summary>Is this module named in the matrix at all?</summary>
    public static bool Knows(string module) => AllowedProviders.ContainsKey(module);
}
