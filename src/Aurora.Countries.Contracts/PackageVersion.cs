using System;
using System.Text.RegularExpressions;
using Aurora.SharedKernel;

namespace Aurora.Countries.Contracts;

/// <summary>
/// A Country Package's own SemVer version, such as <c>1.4.0</c> (ADR-0008 §3.1).
/// </summary>
/// <remarks>
/// <para>
/// This type validates the <i>shape</i> of a version and nothing else. It deliberately does not
/// order two versions: ordering SemVer correctly means ordering pre-release identifiers, and the
/// implementation of that already exists in <c>NuGet.Versioning</c>, which the host uses and this
/// assembly must not take a dependency on — every Country Package references this assembly, so
/// every dependency here is a dependency the whole ecosystem carries.
/// </para>
/// <para>
/// Build metadata is stripped on the way in, because SemVer gives it no part in comparison. Keeping
/// it would make <c>1.4.0</c> and <c>1.4.0+ci.42</c> two different installed versions of one
/// package.
/// </para>
/// </remarks>
public readonly partial record struct PackageVersion
{
    private readonly string? _value;

    private PackageVersion(string value) => _value = value;

    /// <summary>The version text, for example <c>1.4.0</c> or <c>2.0.0-rc.1</c>.</summary>
    /// <exception cref="InvalidOperationException">This is the default, unassigned value.</exception>
    public string Value => _value ?? throw Unassigned();

    /// <summary>Whether this value names a version at all, rather than being <c>default</c>.</summary>
    public bool IsSpecified => _value is not null;

    /// <summary>Reads a SemVer 2.0 version, rejecting anything else.</summary>
    public static Result<PackageVersion> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return PackageManifestErrors.Invalid("package.version", "A package version is required.");
        }

        if (!Shape().IsMatch(value))
        {
            return PackageManifestErrors.Invalid(
                "package.version",
                $"'{value}' is not a SemVer 2.0 version such as '1.4.0' or '2.0.0-rc.1'.");
        }

        int buildMetadata = value.IndexOf('+', StringComparison.Ordinal);
        return Result.Success(new PackageVersion(buildMetadata < 0 ? value : value[..buildMetadata]));
    }

    /// <inheritdoc/>
    public override string ToString() => _value ?? "<unspecified package version>";

    // The official SemVer 2.0.0 recommended expression, anchored.
    [GeneratedRegex(
        @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+([0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex Shape();

    private static InvalidOperationException Unassigned() =>
        new("This PackageVersion names no version. It is the default value of the struct, which only " +
            "exists because C# gives every struct one; build versions with PackageVersion.Create.");
}
