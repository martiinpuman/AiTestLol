using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aurora.Countries.Contracts.Localization;
using Aurora.SharedKernel;

namespace Aurora.Countries.Contracts.Documents;

/// <summary>
/// Extension point 4 — how one jurisdiction wants a commercial document expressed and validated
/// before it is sent (ADR-0008 §7).
/// </summary>
/// <typeparam name="TDocument">
/// The canonical document this profile covers — the jurisdiction-neutral
/// <c>Aurora.Documents.Canonical</c> model, one closed implementation per document type.
/// </typeparam>
/// <remarks>
/// <para>
/// This is the EN 16931 pattern, which the research found to be the one genuinely transferable idea
/// in e-invoicing: one canonical semantic model, narrowed per jurisdiction by a usage specification
/// (a CIUS — PINT A-NZ is New Zealand and Australia's). Core never learns UBL, never learns Peppol,
/// and never gains a second invoice model for the second country.
/// </para>
/// <para>
/// It is generic in the document rather than fixed to one type for two reasons. An invoice and a
/// credit note are validated by different rules in the same profile, so a package supplies a profile
/// per document type; and the canonical model itself belongs to <c>Aurora.Documents.Canonical</c>,
/// so this contract does not have to guess its shape in order to exist. The capability map keeps the
/// open generic definition, and a package registers closed implementations.
/// </para>
/// </remarks>
[ExtensionPoint(CountryPackageCapability.EInvoicingProfile)]
public interface IEInvoicingProfile<in TDocument>
    where TDocument : class
{
    /// <summary>
    /// The published identifier of the specification this profile implements, such as a Peppol
    /// customization id. It goes into the document and is what a receiver matches on.
    /// </summary>
    string ProfileId { get; }

    /// <summary>The scheme participants are addressed by in this jurisdiction.</summary>
    ElectronicAddressScheme IdentifierScheme { get; }

    /// <summary>How a document in this profile reaches its recipient.</summary>
    IDocumentTransport Transport { get; }

    /// <summary>
    /// Checks a document against this jurisdiction's business rules, before anything is sent.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Map"/> and always run first: a rejected e-invoice is a rejected
    /// invoice, and finding out from the receiving network days later is materially worse than
    /// finding out at the point of sending, with the rule id to show the user.
    /// </remarks>
    ValidationReport Validate(TDocument document);

    /// <summary>Maps a canonical document into this jurisdiction's wire format.</summary>
    Result<EInvoiceDocument> Map(TDocument document);
}

/// <summary>
/// A scheme electronic addresses are issued under — Peppol's EAS list, for example, where
/// <c>0088</c> is a GS1 GLN and <c>0193</c> is a New Zealand Business Number.
/// </summary>
public readonly record struct ElectronicAddressScheme
{
    private readonly string? _value;

    private ElectronicAddressScheme(string value) => _value = value;

    /// <summary>The scheme identifier.</summary>
    /// <exception cref="InvalidOperationException">This is the default, unassigned value.</exception>
    public string Value => _value ?? throw new InvalidOperationException(
        "This ElectronicAddressScheme names no scheme; build one with ElectronicAddressScheme.Create.");

    /// <summary>Whether this value names a scheme at all, rather than being <c>default</c>.</summary>
    public bool IsSpecified => _value is not null;

    /// <summary>Reads a scheme identifier, rejecting empty text.</summary>
    public static Result<ElectronicAddressScheme> Create(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? PackageManifestErrors.Invalid("eInvoicing.identifierScheme", "A scheme identifier is required.")
            : Result.Success(new ElectronicAddressScheme(value));

    /// <inheritdoc/>
    public override string ToString() => _value ?? "<unspecified address scheme>";
}

/// <summary>Where a document is sent, in the scheme the jurisdiction uses.</summary>
/// <param name="Scheme">The scheme the address is issued under.</param>
/// <param name="Value">The address itself.</param>
public sealed record ElectronicAddress(ElectronicAddressScheme Scheme, string Value);

/// <summary>A canonical document rendered into a jurisdiction's wire format.</summary>
/// <param name="ProfileId">The profile that produced it.</param>
/// <param name="DocumentId">The document's own identifier, for correlation and audit.</param>
/// <param name="MediaType">The IANA media type of <paramref name="Content"/>.</param>
/// <param name="Content">The bytes.</param>
public sealed record EInvoiceDocument(
    string ProfileId,
    string DocumentId,
    string MediaType,
    ReadOnlyMemory<byte> Content);

/// <summary>
/// How a rendered document leaves the system — a network access point, a tax authority gateway, or
/// a file drop.
/// </summary>
/// <remarks>
/// Transport is the one part of a Country Package that talks to the outside world, and it is the
/// part to be most careful about: package code runs in-process with full trust, so a transport can
/// open any socket the process can. That is stated plainly rather than mitigated
/// (ADR-0008 §9.4), and it is the reason v1 loads first-party packages only.
/// </remarks>
public interface IDocumentTransport
{
    /// <summary>A stable id for this transport.</summary>
    string TransportId { get; }

    /// <summary>Sends a document and returns what the receiver said.</summary>
    Task<Result<TransportReceipt>> SendAsync(
        OutboundDocument document,
        CancellationToken cancellationToken);
}

/// <summary>A document handed to a transport.</summary>
/// <param name="Recipient">Where it goes.</param>
/// <param name="DocumentId">The document's identifier, for correlation.</param>
/// <param name="MediaType">The IANA media type of <paramref name="Content"/>.</param>
/// <param name="Content">The bytes.</param>
public sealed record OutboundDocument(
    ElectronicAddress Recipient,
    string DocumentId,
    string MediaType,
    ReadOnlyMemory<byte> Content);

/// <summary>What a receiver said when it accepted a document.</summary>
/// <param name="TransportId">The transport that sent it.</param>
/// <param name="ReferenceId">The receiver's own reference, which is what a dispute is traced by.</param>
/// <param name="AcceptedAt">When the receiver accepted it.</param>
public sealed record TransportReceipt(string TransportId, string ReferenceId, DateTimeOffset AcceptedAt);

/// <summary>How serious a validation finding is.</summary>
public enum ValidationSeverity
{
    /// <summary>Worth telling the user; the document may still be sent.</summary>
    Warning = 1,

    /// <summary>The document must not be sent.</summary>
    Error = 2,
}

/// <summary>One thing a validator found.</summary>
/// <param name="RuleId">
/// The published rule's own identifier, such as a Peppol business rule id. Carried so a user can
/// look the rule up rather than guess what the message meant.
/// </param>
/// <param name="Severity">How serious it is.</param>
/// <param name="Message">What is wrong, in the locales the package ships.</param>
/// <param name="Location">Where in the document, if the validator can say.</param>
public sealed record ValidationFinding(
    string RuleId,
    ValidationSeverity Severity,
    LocalizedText Message,
    string? Location);

/// <summary>What a validator found, and whether the document may be sent.</summary>
public sealed class ValidationReport
{
    private ValidationReport(IReadOnlyList<ValidationFinding> findings) => Findings = findings;

    /// <summary>Everything found, in the order the validator found it.</summary>
    public IReadOnlyList<ValidationFinding> Findings { get; }

    /// <summary>How many findings there are — including the zero that means "nothing was wrong".</summary>
    public int FindingCount => Findings.Count;

    /// <summary>
    /// Whether the document may be sent: true when no finding is an
    /// <see cref="ValidationSeverity.Error"/>.
    /// </summary>
    public bool IsValid => !Findings.Any(finding => finding.Severity == ValidationSeverity.Error);

    /// <summary>A report with nothing wrong.</summary>
    public static ValidationReport Valid { get; } = new([]);

    /// <summary>A report over the findings given.</summary>
    public static ValidationReport Of(IReadOnlyList<ValidationFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        return findings.Count == 0 ? Valid : new ValidationReport(findings);
    }
}
