using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Aurora.Countries.Contracts;
using Aurora.Countries.Contracts.Identifiers;
using Aurora.Countries.Contracts.Taxation;
using Aurora.SharedKernel;

namespace Aurora.Countries.TestPackage;

/// <summary>
/// A Country Package for a jurisdiction that does not exist, shaped exactly like one that does.
/// </summary>
/// <remarks>
/// It parses its own embedded manifest with the contract's own reader rather than restating it in
/// C#, which is what the manifest-matches-assembly check is really asking for: one description of
/// the package, read twice.
/// </remarks>
public sealed class TestlandPackage : ICountryPackage
{
    private readonly TestlandTaxRules _taxRules = new();
    private readonly TestlandRegistrationNumberValidator _identifiers = new();

    /// <summary>Reads the manifest this assembly ships.</summary>
    /// <exception cref="InvalidOperationException">The embedded manifest is missing or invalid.</exception>
    public TestlandPackage() => Manifest = ReadEmbeddedManifest();

    /// <inheritdoc/>
    public CountryPackageManifest Manifest { get; }

    /// <inheritdoc/>
    public object? GetExtension(Type contractType)
    {
        ArgumentNullException.ThrowIfNull(contractType);

        if (contractType == typeof(ITaxRuleProvider))
        {
            return _taxRules;
        }

        return contractType == typeof(IIdentifierValidator) ? _identifiers : null;
    }

    private static CountryPackageManifest ReadEmbeddedManifest()
    {
        Assembly assembly = typeof(TestlandPackage).Assembly;

        using Stream resource =
            assembly.GetManifestResourceStream(CountryPackageManifestJson.ResourceName)
            ?? throw new InvalidOperationException(
                $"{assembly.GetName().Name} embeds no '{CountryPackageManifestJson.ResourceName}'.");

        using MemoryStream buffer = new();
        resource.CopyTo(buffer);

        Result<CountryPackageManifest> manifest = CountryPackageManifestJson.Read(buffer.ToArray());
        return manifest.IsSuccess
            ? manifest.Value
            : throw new InvalidOperationException($"The embedded manifest is invalid: {manifest.Error}");
    }
}

/// <summary>Testland's goods and services tax: 12.5 percent until 2010, 15 percent after.</summary>
/// <remarks>
/// Two versions of one code, meeting exactly at a boundary, because that is the arrangement the
/// effective-dating rules have to survive: the rate on the boundary day is the new one, and the day
/// before it is the old one.
/// </remarks>
public sealed class TestlandTaxRules : ITaxRuleProvider
{
    /// <summary>The day the rate changed.</summary>
    public static readonly DateOnly RateChangeDate = new(2010, 10, 1);

    private readonly Dictionary<TaxCode, List<TaxRuleVersion>> _history;

    /// <summary>Builds the rule set.</summary>
    public TestlandTaxRules()
    {
        TaxCode standard = Code("GST");
        TaxCode zero = Code("ZERO");

        CoveragePeriod = DateRange.FromUntil(new DateOnly(2000, 1, 1), new DateOnly(2100, 1, 1));

        _history = new Dictionary<TaxCode, List<TaxRuleVersion>>
        {
            [standard] =
            [
                Rule(standard, TaxCategory.Standard, 12.5m, CoveragePeriod.Start, RateChangeDate),
                Rule(standard, TaxCategory.Standard, 15m, RateChangeDate, CoveragePeriod.EndExclusive),
            ],
            [zero] =
            [
                Rule(zero, TaxCategory.ZeroRated, 0m, CoveragePeriod.Start, CoveragePeriod.EndExclusive),
            ],
        };

        TaxCodes = new HashSet<TaxCode>(_history.Keys);
    }

    /// <inheritdoc/>
    public DateRange CoveragePeriod { get; }

    /// <inheritdoc/>
    public IReadOnlySet<TaxCode> TaxCodes { get; }

    /// <inheritdoc/>
    public Result<TaxRuleVersion> Resolve(
        TaxCode code,
        CompanyId company,
        TaxRegistrationId registration,
        DateOnly asOf)
    {
        if (!_history.TryGetValue(code, out List<TaxRuleVersion>? versions))
        {
            return TaxErrors.NotCovered($"Testland has no tax code '{code}'.");
        }

        foreach (TaxRuleVersion version in versions)
        {
            if (version.Validity.Contains(asOf))
            {
                return Result.Success(version);
            }
        }

        return TaxErrors.NotCovered($"Testland has no rule for '{code}' on {asOf:O}.");
    }

    /// <inheritdoc/>
    public IReadOnlyList<TaxRuleVersion> History(TaxCode code) =>
        _history.TryGetValue(code, out List<TaxRuleVersion>? versions) ? versions : [];

    private static TaxCode Code(string value)
    {
        Result<TaxCode> code = TaxCode.Create(value);
        return code.IsSuccess ? code.Value : throw new InvalidOperationException(code.Error.ToString());
    }

    private static TaxRuleVersion Rule(
        TaxCode code,
        TaxCategory category,
        decimal percent,
        DateOnly from,
        DateOnly until)
    {
        Result<TaxRate> rate = TaxRate.Create(Percentage.FromPercent(percent));
        Result<PackageVersion> version = PackageVersion.Create("1.4.0");

        if (rate.IsFailure || version.IsFailure)
        {
            throw new InvalidOperationException("The test package's own rules do not validate.");
        }

        return new TaxRuleVersion(
            code,
            category,
            rate.Value,
            TaxBasis.LineNet,
            MidpointRounding.AwayFromZero,
            DateRange.FromUntil(from, until),
            version.Value);
    }
}

/// <summary>
/// Testland's company registration number: eight digits, with the eighth a mod-11 check digit over
/// the first seven weighted 7..2.
/// </summary>
/// <remarks>
/// A made-up scheme with a real shape. It exists so the identifier extension point is exercised by
/// something that can actually reject a value, rather than by a validator that says yes to
/// everything and proves nothing.
/// </remarks>
public sealed class TestlandRegistrationNumberValidator : IIdentifierValidator
{
    /// <inheritdoc/>
    public IdentifierKind Kind => IdentifierKind.CompanyRegistrationNumber;

    /// <inheritdoc/>
    public string CountryCode => "NZ";

    /// <inheritdoc/>
    public IdentifierValidation Validate(string candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        string digits = candidate.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);

        if (digits.Length != 8)
        {
            return Reject($"A Testland registration number is 8 digits; '{candidate}' has {digits.Length}.");
        }

        int sum = 0;
        for (int position = 0; position < 7; position++)
        {
            if (!char.IsAsciiDigit(digits[position]))
            {
                return Reject($"'{candidate}' is not all digits.");
            }

            sum += (digits[position] - '0') * (7 - position);
        }

        if (!char.IsAsciiDigit(digits[7]))
        {
            return Reject($"'{candidate}' is not all digits.");
        }

        int expected = (11 - (sum % 11)) % 11;
        return expected == 10
            ? Reject($"'{candidate}' has no valid check digit under the Testland scheme.")
            : expected == digits[7] - '0'
                ? IdentifierValidation.Verified(digits)
                : Reject($"'{candidate}' fails its check digit: expected {expected}, found {digits[7]}.");
    }

    /// <summary>The check digit for the first seven digits of <paramref name="body"/>.</summary>
    /// <remarks>
    /// Public so the test that proves the validator can reject does not have to hand-compute one,
    /// and so a wrong check digit in a test vector is impossible rather than merely unlikely.
    /// </remarks>
    public static int CheckDigitFor(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (body.Length != 7)
        {
            throw new ArgumentException("A Testland registration number body is 7 digits.", nameof(body));
        }

        int sum = 0;
        for (int position = 0; position < 7; position++)
        {
            sum += (body[position] - '0') * (7 - position);
        }

        return (11 - (sum % 11)) % 11;
    }

    private static IdentifierValidation Reject(string why)
    {
        Result<Aurora.Countries.Contracts.Localization.LocalizedText> reason =
            Aurora.Countries.Contracts.Localization.LocalizedText.Invariant(why);

        return IdentifierValidation.Rejected(reason.Value);
    }
}
