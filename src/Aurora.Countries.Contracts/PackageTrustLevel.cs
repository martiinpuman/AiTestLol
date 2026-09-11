using System.Diagnostics.CodeAnalysis;

namespace Aurora.Countries.Contracts;

/// <summary>
/// How much a Country Package is trusted (ADR-0008 §9.3).
/// </summary>
/// <remarks>
/// <para>
/// A manifest carries this value as <see cref="CountryPackageManifest.DeclaredTrust"/> — the word
/// <i>declared</i> is load-bearing. A manifest is bytes inside the package, so a package saying
/// <see cref="FirstParty"/> proves nothing at all; only a signature verified against a key in the
/// platform trust store establishes trust. The host compares what was declared with what it
/// established and refuses the package when the declaration claims more.
/// </para>
/// <para>
/// None of these levels is a sandbox. Loaded package code runs in-process with full trust
/// (ADR-0008 §9.4); trust here decides <i>whether</i> code runs, never what it can do once it does.
/// </para>
/// </remarks>
[SuppressMessage(
    "Naming",
    "CA1720:Identifier contains type name",
    Justification = "'Unsigned' is the trust level ADR-0008 §9.3 names, and the word an operator " +
        "reads in a manifest and in an audit event. Renaming it to avoid a collision with the " +
        "CLR's unsigned integer types would put the code and the decision record out of step.")]
public enum PackageTrustLevel
{
    /// <summary>
    /// No valid signature was established. Refused unless the host is explicitly configured to allow
    /// unsigned packages, which it honours only outside production.
    /// </summary>
    Unsigned = 0,

    /// <summary>Signed by a key an operator added to the trust store, and recorded when they did.</summary>
    Partner = 1,

    /// <summary>Signed by Aurora's own release key. Reviewed like core code and shipped in the image.</summary>
    FirstParty = 2,
}
