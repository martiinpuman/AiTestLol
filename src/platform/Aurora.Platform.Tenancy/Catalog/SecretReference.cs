using System;

namespace Aurora.Platform.Tenancy.Catalog;

/// <summary>
/// Where a secret lives — never what it is. <c>vault://kv/aurora/clusters/nz-1/admin</c>,
/// <c>env:AURORA_NZ1_APP_PASSWORD</c> — the value of <c>catalog.database_cluster.*_secret_ref</c>
/// (ADR-0007 §9.2, ADR-0011 tier 2).
/// </summary>
/// <remarks>
/// <para>
/// The catalog is backed up, dumped for support and read by every request. A credential in it
/// would be in every one of those places. So the catalog holds a <em>reference</em> that the
/// connection resolver (B-06) hands to the host's secret store at runtime, and the connection
/// string is composed in memory and never stored.
/// </para>
/// <para>
/// <b>This type is a tripwire, not a guarantee.</b> It refuses the shapes a credential most often
/// arrives in by mistake — anything containing <c>=</c> or <c>;</c> (every Npgsql connection
/// string has both), whitespace or a control character — and the catalog repeats the same check
/// as a constraint. A bare password that happens to avoid those characters would pass. What
/// stops that is the rule that nothing in this module ever reads a secret's value out of the
/// catalog, only a reference into the store.
/// </para>
/// </remarks>
internal readonly record struct SecretReference
{
    public const int MaxLength = 256;

    private readonly string? _value;

    private SecretReference(string value) => _value = value;

    public bool IsSpecified => _value is not null;

    /// <exception cref="InvalidOperationException">The reference is unspecified.</exception>
    public string Value => _value ?? throw Unspecified();

    /// <exception cref="ArgumentNullException"><paramref name="reference"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="reference"/> is not a well-formed reference.</exception>
    public static SecretReference Of(string reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        if (!IsWellFormed(reference))
        {
            throw new ArgumentException(
                "A secret reference names where a secret lives in the host's secret store; it never " +
                $"holds the secret. It is 1 to {MaxLength} characters with no whitespace, no control " +
                "characters, and neither '=' nor ';' - the characters a connection string or a " +
                "'key=value' credential would carry. The value given does not satisfy that.",
                nameof(reference));
        }

        return new SecretReference(reference);
    }

    public static bool IsWellFormed(string? reference)
    {
        if (string.IsNullOrEmpty(reference) || reference.Length > MaxLength)
        {
            return false;
        }

        foreach (char character in reference)
        {
            if (character is '=' or ';' || char.IsWhiteSpace(character) || char.IsControl(character))
            {
                return false;
            }
        }

        return true;
    }

    public override string ToString() => _value ?? "<unspecified secret reference>";

    private static InvalidOperationException Unspecified() =>
        new("The secret reference is unspecified. A SecretReference obtained from `default` names " +
            "nothing; build one with SecretReference.Of(reference).");
}
