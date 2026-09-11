using System;
using System.Text.RegularExpressions;
using Aurora.SharedKernel;

namespace Aurora.Countries.Contracts;

/// <summary>
/// The short key a Country Package owns, such as <c>nz</c> — and, through it, the one PostgreSQL
/// schema the package may create objects in: <c>pkg_nz</c> (ADR-0008 §4.1 R1).
/// </summary>
/// <remarks>
/// <para>
/// This value reaches the database as an <b>identifier inside DDL</b>, where a parameter cannot go:
/// <c>create schema pkg_nz</c> has no placeholder for the schema name. The only defence available
/// at that point is that the value was never able to be anything but a short lowercase word in the
/// first place, so the shape below is enforced on the way in, once, and the schema name is derived
/// rather than read from the manifest's own <c>schema</c> field.
/// </para>
/// <para>
/// It is also the reason a package cannot claim a schema it does not own: two packages with the
/// same key collide on <see cref="SchemaName"/>, and the installer refuses the second.
/// </para>
/// </remarks>
public readonly partial record struct PackageKey
{
    /// <summary>The prefix every package schema carries, so core schemas are never in reach.</summary>
    public const string SchemaPrefix = "pkg_";

    private const int MaxLength = 16;

    private readonly string? _value;

    private PackageKey(string value) => _value = value;

    /// <summary>The key itself, for example <c>nz</c>.</summary>
    /// <exception cref="InvalidOperationException">This is the default, unassigned value.</exception>
    public string Value => _value ?? throw Unassigned();

    /// <summary>Whether this value names a key at all, rather than being <c>default</c>.</summary>
    public bool IsSpecified => _value is not null;

    /// <summary>
    /// The package's schema in the tenant database, for example <c>pkg_nz</c>. Derived, never read
    /// from the manifest.
    /// </summary>
    /// <exception cref="InvalidOperationException">This is the default, unassigned value.</exception>
    public string SchemaName => SchemaPrefix + Value;

    /// <summary>
    /// Reads a package key, rejecting anything that is not a short lowercase identifier — which,
    /// because <see cref="SchemaName"/> is built from it, is also what keeps the schema name out of
    /// reach of anything that could be read as SQL.
    /// </summary>
    public static Result<PackageKey> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return PackageManifestErrors.Invalid("package.key", "A package key is required.");
        }

        if (value.Length > MaxLength)
        {
            return PackageManifestErrors.Invalid(
                "package.key",
                $"A package key may be at most {MaxLength} characters, because '{SchemaPrefix}' plus " +
                $"the key must stay inside PostgreSQL's 63-byte identifier limit; '{value}' is " +
                $"{value.Length}.");
        }

        return Shape().IsMatch(value)
            ? Result.Success(new PackageKey(value))
            : PackageManifestErrors.Invalid(
                "package.key",
                $"'{value}' is not a package key. Expected a lowercase letter followed by lowercase " +
                $"letters, digits or underscores, such as 'nz' — the key becomes the schema name " +
                $"'{SchemaPrefix}<key>' in DDL, where nothing can be parameterised.");
    }

    /// <inheritdoc/>
    public override string ToString() => _value ?? "<unspecified package key>";

    [GeneratedRegex(@"^[a-z][a-z0-9_]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();

    private static InvalidOperationException Unassigned() =>
        new("This PackageKey names no package. It is the default value of the struct, which only " +
            "exists because C# gives every struct one; build package keys with PackageKey.Create.");
}
