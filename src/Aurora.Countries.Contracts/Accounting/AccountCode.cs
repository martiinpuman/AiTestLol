using System;
using System.Text.RegularExpressions;
using Aurora.SharedKernel;

namespace Aurora.Countries.Contracts.Accounting;

/// <summary>
/// An account's code within a chart of accounts, as the jurisdiction writes it.
/// </summary>
/// <remarks>
/// Text, not a number. Swedish BAS codes are four digits, New Zealand charts are commonly
/// alphanumeric with dashes, and some jurisdictions use segments. Anything that parsed this as an
/// integer would work until the first chart that does not, and the first chart that does not would
/// be somebody's live ledger.
/// </remarks>
public readonly partial record struct AccountCode
{
    private const int MaxLength = 32;

    private readonly string? _value;

    private AccountCode(string value) => _value = value;

    /// <summary>The code itself, for example <c>1-1100</c>.</summary>
    /// <exception cref="InvalidOperationException">This is the default, unassigned value.</exception>
    public string Value => _value ?? throw Unassigned();

    /// <summary>Whether this value names an account at all, rather than being <c>default</c>.</summary>
    public bool IsSpecified => _value is not null;

    /// <summary>Reads an account code, rejecting whitespace, control characters and empty text.</summary>
    public static Result<AccountCode> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return PackageManifestErrors.Invalid("account.code", "An account code is required.");
        }

        if (value.Length > MaxLength)
        {
            return PackageManifestErrors.Invalid(
                "account.code",
                $"An account code may be at most {MaxLength} characters; '{value}' is {value.Length}.");
        }

        return Shape().IsMatch(value)
            ? Result.Success(new AccountCode(value))
            : PackageManifestErrors.Invalid(
                "account.code",
                $"'{value}' is not an account code. Expected letters, digits, dots, dashes or " +
                $"underscores with no spaces.");
    }

    /// <inheritdoc/>
    public override string ToString() => _value ?? "<unspecified account code>";

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._\-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();

    private static InvalidOperationException Unassigned() =>
        new("This AccountCode names no account. It is the default value of the struct, which only " +
            "exists because C# gives every struct one; build codes with AccountCode.Create.");
}
