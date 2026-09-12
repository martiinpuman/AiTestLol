using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Aurora.SharedKernel;

namespace Aurora.Countries.Contracts;

/// <summary>
/// Reads <c>package.manifest.json</c> — the embedded resource form of a
/// <see cref="CountryPackageManifest"/> (ADR-0008 §3.2).
/// </summary>
/// <remarks>
/// <para>
/// It lives in the contracts assembly rather than in the host so that a package and the host parse
/// the manifest with the <b>same</b> reader. A package whose <c>ICountryPackage.Manifest</c> is
/// hand-built in C# has two descriptions of itself that can disagree; one parser means the package
/// contract test is checking that the code implements what the manifest says, not that two authors
/// typed the same thing twice.
/// </para>
/// <para>
/// Reading is strict in both directions. A missing required field is a rejection, and so is a field
/// this core contract version does not know: an unknown field means the package was built against a
/// newer contract, and silently ignoring it is how a package ends up half-installed with a rule the
/// host never applied. The package's <c>coreContractRange</c> is what is supposed to catch that
/// first; this is the backstop that makes the failure loud if it does not.
/// </para>
/// </remarks>
public static class CountryPackageManifestJson
{
    /// <summary>The resource name a package embeds its manifest under.</summary>
    public const string ResourceName = "package.manifest.json";

    private static readonly string[] KnownFields =
    [
        "id", "key", "displayName", "version", "coreContractRange", "jurisdiction",
        "capabilities", "schema", "dependsOn", "conflictsWith", "localeOnly", "publisher", "trust",
    ];

    /// <summary>Parses and validates a manifest from its UTF-8 JSON bytes.</summary>
    public static Result<CountryPackageManifest> Read(ReadOnlySpan<byte> utf8Json)
    {
        JsonDocument document;
        try
        {
            Utf8JsonReader reader = new(utf8Json, new JsonReaderOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = false,
            });
            document = JsonDocument.ParseValue(ref reader);
        }
        catch (JsonException failure)
        {
            return PackageManifestErrors.Invalid(ResourceName, $"is not valid JSON: {failure.Message}");
        }

        using (document)
        {
            return Read(document.RootElement);
        }
    }

    private static Result<CountryPackageManifest> Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return PackageManifestErrors.Invalid(ResourceName, "must be a JSON object.");
        }

        Result unknown = RejectUnknownFields(root, KnownFields, ResourceName);
        if (unknown.IsFailure)
        {
            return unknown.Error;
        }

        Result<PackageId> id = PackageId.Create(Text(root, "id"));
        if (id.IsFailure)
        {
            return id.Error;
        }

        Result<PackageKey> key = PackageKey.Create(Text(root, "key"));
        if (key.IsFailure)
        {
            return key.Error;
        }

        Result<PackageVersion> version = PackageVersion.Create(Text(root, "version"));
        if (version.IsFailure)
        {
            return version.Error;
        }

        Result<Jurisdiction> jurisdiction = ReadJurisdiction(root);
        if (jurisdiction.IsFailure)
        {
            return jurisdiction.Error;
        }

        Result<List<CountryPackageCapability>> capabilities = ReadCapabilities(root);
        if (capabilities.IsFailure)
        {
            return capabilities.Error;
        }

        Result<List<PackageDependency>> dependsOn = ReadDependencies(root);
        if (dependsOn.IsFailure)
        {
            return dependsOn.Error;
        }

        Result<List<PackageId>> conflictsWith = ReadConflicts(root);
        if (conflictsWith.IsFailure)
        {
            return conflictsWith.Error;
        }

        Result<bool> localeOnly = ReadBoolean(root, "localeOnly");
        if (localeOnly.IsFailure)
        {
            return localeOnly.Error;
        }

        Result<PackageTrustLevel> trust = ReadTrust(root);
        if (trust.IsFailure)
        {
            return trust.Error;
        }

        return CountryPackageManifest.Create(
            id.Value,
            key.Value,
            Text(root, "displayName"),
            version.Value,
            Text(root, "coreContractRange"),
            Text(root, "schema"),
            jurisdiction.Value,
            capabilities.Value,
            dependsOn.Value,
            conflictsWith.Value,
            localeOnly.Value,
            Text(root, "publisher"),
            trust.Value);
    }

    private static Result<Jurisdiction> ReadJurisdiction(JsonElement root)
    {
        if (!root.TryGetProperty("jurisdiction", out JsonElement element)
            || element.ValueKind != JsonValueKind.Object)
        {
            return PackageManifestErrors.Invalid("jurisdiction", "is required and must be an object.");
        }

        Result unknown = RejectUnknownFields(
            element,
            ["countryCode", "defaultCurrency", "locales"],
            "jurisdiction");
        if (unknown.IsFailure)
        {
            return unknown.Error;
        }

        Result<List<string>> locales = ReadStringArray(element, "locales", "jurisdiction.locales");
        return locales.IsFailure
            ? locales.Error
            : Jurisdiction.Create(Text(element, "countryCode"), Text(element, "defaultCurrency"), locales.Value);
    }

    private static Result<List<CountryPackageCapability>> ReadCapabilities(JsonElement root)
    {
        Result<List<string>> names = ReadStringArray(root, "capabilities", "capabilities");
        if (names.IsFailure)
        {
            return names.Error;
        }

        List<CountryPackageCapability> capabilities = new(names.Value.Count);
        foreach (string name in names.Value)
        {
            if (!Enum.TryParse(name, ignoreCase: false, out CountryPackageCapability capability)
                || !Enum.IsDefined(capability))
            {
                return PackageManifestErrors.Invalid(
                    "capabilities",
                    $"'{name}' is not a capability this core contract version declares. Known " +
                    $"capabilities: {string.Join(", ", CountryPackageCapabilities.All)}.");
            }

            if (capabilities.Contains(capability))
            {
                return PackageManifestErrors.Invalid("capabilities", $"'{name}' is declared twice.");
            }

            capabilities.Add(capability);
        }

        return Result.Success(capabilities);
    }

    private static Result<List<PackageDependency>> ReadDependencies(JsonElement root)
    {
        List<PackageDependency> dependencies = [];
        if (!root.TryGetProperty("dependsOn", out JsonElement element))
        {
            return Result.Success(dependencies);
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            return PackageManifestErrors.Invalid("dependsOn", "must be an array.");
        }

        foreach (JsonElement entry in element.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                return PackageManifestErrors.Invalid("dependsOn", "entries must be objects.");
            }

            Result unknown = RejectUnknownFields(entry, ["id", "range"], "dependsOn");
            if (unknown.IsFailure)
            {
                return unknown.Error;
            }

            Result<PackageDependency> dependency =
                PackageDependency.Create(Text(entry, "id"), Text(entry, "range"));
            if (dependency.IsFailure)
            {
                return dependency.Error;
            }

            dependencies.Add(dependency.Value);
        }

        return Result.Success(dependencies);
    }

    private static Result<List<PackageId>> ReadConflicts(JsonElement root)
    {
        List<PackageId> conflicts = [];
        if (!root.TryGetProperty("conflictsWith", out JsonElement element))
        {
            return Result.Success(conflicts);
        }

        Result<List<string>> names = ReadStringArray(root, "conflictsWith", "conflictsWith");
        if (names.IsFailure)
        {
            return names.Error;
        }

        foreach (string name in names.Value)
        {
            Result<PackageId> id = PackageId.Create(name);
            if (id.IsFailure)
            {
                return id.Error;
            }

            conflicts.Add(id.Value);
        }

        return Result.Success(conflicts);
    }

    private static Result<PackageTrustLevel> ReadTrust(JsonElement root)
    {
        string? name = Text(root, "trust");
        if (string.IsNullOrWhiteSpace(name))
        {
            return PackageManifestErrors.Invalid(
                "trust",
                $"is required. State what the package claims to be — one of " +
                $"{string.Join(", ", Enum.GetNames<PackageTrustLevel>())} — knowing that the host " +
                $"establishes trust from the signature and refuses a claim it cannot match.");
        }

        return Enum.TryParse(name, ignoreCase: false, out PackageTrustLevel trust) && Enum.IsDefined(trust)
            ? Result.Success(trust)
            : PackageManifestErrors.Invalid("trust", $"'{name}' is not a trust level.");
    }

    private static Result<bool> ReadBoolean(JsonElement root, string field)
    {
        if (!root.TryGetProperty(field, out JsonElement element))
        {
            return Result.Success(false);
        }

        return element.ValueKind switch
        {
            JsonValueKind.True => Result.Success(true),
            JsonValueKind.False => Result.Success(false),
            _ => PackageManifestErrors.Invalid(field, "must be true or false."),
        };
    }

    private static Result<List<string>> ReadStringArray(JsonElement parent, string field, string path)
    {
        if (!parent.TryGetProperty(field, out JsonElement element)
            || element.ValueKind != JsonValueKind.Array)
        {
            return PackageManifestErrors.Invalid(path, "is required and must be an array of strings.");
        }

        List<string> values = [];
        foreach (JsonElement entry in element.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.String)
            {
                return PackageManifestErrors.Invalid(path, "must hold strings only.");
            }

            values.Add(entry.GetString()!);
        }

        return Result.Success(values);
    }

    private static Result RejectUnknownFields(JsonElement element, string[] known, string path)
    {
        string[] unknown =
        [
            .. element.EnumerateObject()
                .Select(property => property.Name)
                .Where(name => !known.Contains(name, StringComparer.Ordinal)),
        ];

        return unknown.Length == 0
            ? Result.Success()
            : PackageManifestErrors.Invalid(
                path,
                $"has field(s) this core contract version does not know: " +
                $"{string.Join(", ", unknown)}. Known fields: {string.Join(", ", known)}.");
    }

    private static string? Text(JsonElement parent, string field) =>
        parent.TryGetProperty(field, out JsonElement element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
}
