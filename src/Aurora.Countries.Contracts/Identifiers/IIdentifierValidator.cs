using System;
using Aurora.Countries.Contracts.Localization;

namespace Aurora.Countries.Contracts.Identifiers;

/// <summary>What kind of identifier is being checked.</summary>
public enum IdentifierKind
{
    /// <summary>A company's registration number in its jurisdiction's register.</summary>
    CompanyRegistrationNumber = 1,

    /// <summary>A tax identification number — VAT number, GST number, TIN.</summary>
    TaxId = 2,

    /// <summary>A bank account identifier.</summary>
    BankAccount = 3,
}

/// <summary>What a check concluded.</summary>
public enum IdentifierStatus
{
    /// <summary>
    /// Nobody checked. The value is recorded and is not to be treated as correct.
    /// </summary>
    Unverified = 0,

    /// <summary>The value is well-formed and its checksum holds.</summary>
    Verified = 1,

    /// <summary>The value is not valid in this jurisdiction.</summary>
    Rejected = 2,
}

/// <summary>
/// Extension point 7 — validating a jurisdiction's identifiers (ADR-0008 §7).
/// </summary>
/// <remarks>
/// <para>
/// A pure function with a published algorithm behind it: the NZBN is a GS1 Global Location Number
/// with a documented check digit, an ABN has its own weighting, an IBAN has ISO 7064 mod 97. Core's
/// <c>OrganizationNumber</c> and its relatives delegate here and are deliberately
/// <b>unvalidatable without a package</b>.
/// </para>
/// <para>
/// Which is why <see cref="IdentifierStatus.Unverified"/> exists and is the zero value. A tenant
/// operating in a jurisdiction nobody has written a package for still needs to record their
/// customers' registration numbers; what they must never get is a silent pass that looks like a
/// check. Core answers <see cref="IdentifierValidation.NoValidatorInstalled"/> in that case — a
/// third state, visible in the UI and in an export, rather than a <see langword="true"/> nobody
/// earned.
/// </para>
/// </remarks>
[ExtensionPoint(CountryPackageCapability.IdentifierValidator)]
public interface IIdentifierValidator
{
    /// <summary>Which kind of identifier this validator understands.</summary>
    IdentifierKind Kind { get; }

    /// <summary>The ISO 3166-1 alpha-2 country whose identifiers it understands.</summary>
    string CountryCode { get; }

    /// <summary>
    /// Checks <paramref name="candidate"/>, returning the normalised form when it holds and a
    /// reason when it does not.
    /// </summary>
    /// <remarks>
    /// Never throws for a malformed value: a user typing their supplier's tax number wrong is an
    /// expected event, not an exceptional one.
    /// </remarks>
    IdentifierValidation Validate(string candidate);
}

/// <summary>What a validator concluded about one value.</summary>
public sealed class IdentifierValidation
{
    private IdentifierValidation(
        IdentifierStatus status,
        string? normalizedValue,
        LocalizedText? reason)
    {
        Status = status;
        NormalizedValue = normalizedValue;
        Reason = reason;
    }

    /// <summary>The conclusion.</summary>
    public IdentifierStatus Status { get; }

    /// <summary>
    /// The value in the form it should be stored in — spaces and punctuation removed, case settled.
    /// Present only when <see cref="Status"/> is <see cref="IdentifierStatus.Verified"/>.
    /// </summary>
    public string? NormalizedValue { get; }

    /// <summary>Why, when the value was rejected or could not be checked.</summary>
    public LocalizedText? Reason { get; }

    /// <summary>The value holds. <paramref name="normalizedValue"/> is how to store it.</summary>
    public static IdentifierValidation Verified(string normalizedValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedValue);
        return new IdentifierValidation(IdentifierStatus.Verified, normalizedValue, null);
    }

    /// <summary>The value does not hold, and here is why.</summary>
    public static IdentifierValidation Rejected(LocalizedText reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        return new IdentifierValidation(IdentifierStatus.Rejected, null, reason);
    }

    /// <summary>
    /// Nobody checked, and here is why — a validator that does not cover this case, or a value from
    /// a system that could not be reached.
    /// </summary>
    public static IdentifierValidation Unverified(LocalizedText reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        return new IdentifierValidation(IdentifierStatus.Unverified, null, reason);
    }

    /// <summary>
    /// The answer core gives when no Country Package for this jurisdiction is installed: recorded,
    /// unchecked, and saying so.
    /// </summary>
    /// <remarks>
    /// This is the documented "unverified" state of ADR-0008 §7 point 7. It exists as a factory
    /// rather than as a comment so that the one place core has to produce it cannot accidentally
    /// produce something that reads as a pass.
    /// </remarks>
    public static IdentifierValidation NoValidatorInstalled(IdentifierKind kind, string countryCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(countryCode);

        string message =
            $"No Country Package installed for {countryCode} validates a {kind}. The value is stored " +
            $"as entered and has not been checked.";

        return Unverified(LocalizedText.Invariant(message).Value);
    }
}
