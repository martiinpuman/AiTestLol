using System;
using System.Text.RegularExpressions;
using Aurora.SharedKernel;

namespace Aurora.Countries.Contracts;

/// <summary>
/// The stable identity of a Country Package, such as <c>aurora.country.nz</c> (ADR-0008 §3.1).
/// </summary>
/// <remarks>
/// Reverse-dotted, lowercase and ASCII, because this string is a directory name on disk
/// (<c>./packages/&lt;id&gt;/&lt;version&gt;/</c>), a row key in <c>catalog.installed_package</c> and
/// part of every log line about a package. A value that can vary by case or carry a path separator
/// is a value that can name two different packages, or a directory outside the package root.
/// </remarks>
public readonly partial record struct PackageId
{
    private const int MaxLength = 128;

    private readonly string? _value;

    private PackageId(string value) => _value = value;

    /// <summary>The identifier text, for example <c>aurora.country.nz</c>.</summary>
    /// <exception cref="InvalidOperationException">This is the default, unassigned value.</exception>
    public string Value => _value ?? throw Unassigned();

    /// <summary>Whether this value names a package at all, rather than being <c>default</c>.</summary>
    public bool IsSpecified => _value is not null;

    /// <summary>
    /// Reads a package identifier, rejecting anything that is not two or more lowercase
    /// alphanumeric segments separated by single dots.
    /// </summary>
    public static Result<PackageId> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return PackageManifestErrors.Invalid("package.id", "A package id is required.");
        }

        if (value.Length > MaxLength)
        {
            return PackageManifestErrors.Invalid(
                "package.id",
                $"A package id may be at most {MaxLength} characters; '{value}' is {value.Length}.");
        }

        return Shape().IsMatch(value)
            ? Result.Success(new PackageId(value))
            : PackageManifestErrors.Invalid(
                "package.id",
                $"'{value}' is not a package id. Expected two or more lowercase alphanumeric " +
                $"segments separated by single dots, such as 'aurora.country.nz'.");
    }

    /// <inheritdoc/>
    public override string ToString() => _value ?? "<unspecified package id>";

    [GeneratedRegex(@"^[a-z0-9]+(?:\.[a-z0-9]+)+$", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();

    private static InvalidOperationException Unassigned() =>
        new("This PackageId names no package. It is the default value of the struct, which only " +
            "exists because C# gives every struct one; build package ids with PackageId.Create.");
}
