namespace Aurora.Countries.Contracts;

/// <summary>
/// What a Country Package contributes. Each member names exactly one extension-point interface in
/// this assembly (ADR-0008 §7); <see cref="CountryPackageCapabilities.ContractTypeFor"/> is the map.
/// </summary>
/// <remarks>
/// <para>
/// A package declares its capabilities in its manifest, and the installer refuses a package whose
/// declared capabilities it cannot resolve to working implementations. Declaring a capability is
/// therefore a promise the host checks, not a label.
/// </para>
/// <para>
/// Adding a member here is a MINOR core-contract bump (ADR-0008 §3.1): an older package never names
/// the new capability, so nothing it declares changes meaning. Removing one, or changing what one
/// means, is MAJOR — and reaches the whole fleet.
/// </para>
/// </remarks>
public enum CountryPackageCapability
{
    /// <summary>A chart of accounts to seed, and the account role mapping over it.</summary>
    ChartOfAccountsTemplate = 1,

    /// <summary>Effective-dated tax codes, rates and categories.</summary>
    TaxRuleSet = 2,

    /// <summary>Statutory report definitions and the formats they are filed in.</summary>
    StatutoryReport = 3,

    /// <summary>An e-invoicing profile: mapping, validation, identifier scheme and transport.</summary>
    EInvoicingProfile = 4,

    /// <summary>Outbound bank payment file formats.</summary>
    PaymentFileFormat = 5,

    /// <summary>Inbound bank statement formats.</summary>
    StatementImportFormat = 6,

    /// <summary>Accounting-data interchange, both export and import.</summary>
    InterchangeFormat = 7,

    /// <summary>Validation of jurisdiction identifiers: registration numbers, tax ids, bank accounts.</summary>
    IdentifierValidator = 8,

    /// <summary>Locales, translated strings and format overrides. Shippable on its own.</summary>
    LocalePack = 9,

    /// <summary>How long records must be kept, and whether erasure defers until they lapse.</summary>
    RetentionPolicy = 10,
}
