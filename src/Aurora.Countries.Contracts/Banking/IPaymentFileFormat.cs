using System;
using System.Collections.Generic;
using System.Linq;
using Aurora.Countries.Contracts.Localization;
using Aurora.SharedKernel;

namespace Aurora.Countries.Contracts.Banking;

/// <summary>
/// Extension point 5 — the file format a jurisdiction's banks accept payment instructions in
/// (ADR-0008 §7).
/// </summary>
/// <remarks>
/// An adapter, and only an adapter. The payments domain model does not change per country: core
/// builds a <see cref="PaymentInstructionBatch"/> in neutral terms and the package renders it. The
/// day this interface starts carrying country-shaped fields is the day the seam has failed.
/// </remarks>
[ExtensionPoint(CountryPackageCapability.PaymentFileFormat)]
public interface IPaymentFileFormat
{
    /// <summary>A stable id for this format, such as a published bank file specification.</summary>
    string FormatId { get; }

    /// <summary>The format's name as a user choosing an export sees it.</summary>
    LocalizedText Name { get; }

    /// <summary>Renders a batch into the bank's file format.</summary>
    Result<PaymentFile> Render(PaymentInstructionBatch batch);
}

/// <summary>
/// Extension point 5, inbound half — parsing a bank statement a jurisdiction's banks produce.
/// </summary>
[ExtensionPoint(CountryPackageCapability.StatementImportFormat)]
public interface IStatementImportFormat
{
    /// <summary>A stable id for this format.</summary>
    string FormatId { get; }

    /// <summary>File extensions this format is usually delivered with, lowercase and with the dot.</summary>
    IReadOnlyList<string> FileExtensions { get; }

    /// <summary>
    /// Whether this format recognises the content — used to pick a parser when a user uploads a file
    /// without saying what it is.
    /// </summary>
    bool CanRead(ReadOnlyMemory<byte> content);

    /// <summary>Parses a statement, or says why it could not.</summary>
    Result<BankStatement> Parse(ReadOnlyMemory<byte> content);
}

/// <summary>
/// How a bank account is identified, in whatever scheme the jurisdiction uses — IBAN, BSB and
/// account number, sort code and account number, or a bank's own.
/// </summary>
/// <param name="Identifier">The account identifier, as the jurisdiction writes it.</param>
/// <param name="Scheme">
/// The scheme it is written in, such as <c>IBAN</c>. Null when the jurisdiction has only one and
/// naming it would be noise.
/// </param>
/// <param name="BankIdentifier">The bank, where the scheme carries it separately — a BIC, say.</param>
public sealed record BankAccountReference(string Identifier, string? Scheme, string? BankIdentifier);

/// <summary>One payment to make.</summary>
/// <param name="EndToEndId">
/// The reference that survives the whole journey, so the statement line that comes back can be
/// matched to the instruction that went out. Without it, reconciliation is guesswork.
/// </param>
/// <param name="Creditor">Who is paid.</param>
/// <param name="CreditorName">Their name, as it must appear on the payment.</param>
/// <param name="Amount">How much.</param>
/// <param name="RemittanceInformation">What the payment is for, if the format carries it.</param>
public sealed record PaymentInstruction(
    string EndToEndId,
    BankAccountReference Creditor,
    string CreditorName,
    Money Amount,
    string? RemittanceInformation);

/// <summary>A batch of payments, in one currency, from one account, on one day.</summary>
public sealed class PaymentInstructionBatch
{
    private PaymentInstructionBatch(
        CompanyId company,
        BankAccountReference debtor,
        DateOnly requestedExecutionDate,
        IReadOnlyList<PaymentInstruction> instructions,
        Money total)
    {
        Company = company;
        Debtor = debtor;
        RequestedExecutionDate = requestedExecutionDate;
        Instructions = instructions;
        Total = total;
    }

    /// <summary>The company paying.</summary>
    public CompanyId Company { get; }

    /// <summary>The account paid from.</summary>
    public BankAccountReference Debtor { get; }

    /// <summary>The day the batch is to be executed.</summary>
    public DateOnly RequestedExecutionDate { get; }

    /// <summary>The payments.</summary>
    public IReadOnlyList<PaymentInstruction> Instructions { get; }

    /// <summary>
    /// The sum of the instructions, computed here rather than taken on trust.
    /// </summary>
    /// <remarks>
    /// Most bank file formats carry a control total, and a format that computed its own would be a
    /// second answer to a question with one right answer. Core computes it once, from the same
    /// instructions the file is rendered from.
    /// </remarks>
    public Money Total { get; }

    /// <summary>
    /// Builds a batch, refusing an empty one, a mixed-currency one, a non-positive amount, or a
    /// duplicate end-to-end reference.
    /// </summary>
    /// <remarks>
    /// A duplicate end-to-end id is refused because it is how a payment gets made twice: the bank
    /// takes both, and the second one reconciles against the first one's invoice.
    /// </remarks>
    public static Result<PaymentInstructionBatch> Create(
        CompanyId company,
        BankAccountReference debtor,
        DateOnly requestedExecutionDate,
        IReadOnlyList<PaymentInstruction> instructions)
    {
        ArgumentNullException.ThrowIfNull(debtor);
        ArgumentNullException.ThrowIfNull(instructions);

        if (instructions.Count == 0)
        {
            return BankingErrors.Invalid("payment.batch", "A batch with no payments pays nobody.");
        }

        Currency currency = instructions[0].Amount.Currency;
        HashSet<string> references = new(StringComparer.Ordinal);

        foreach (PaymentInstruction instruction in instructions)
        {
            if (string.IsNullOrWhiteSpace(instruction.EndToEndId))
            {
                return BankingErrors.Invalid(
                    "payment.endToEndId",
                    "Every payment needs an end-to-end reference; without one the statement line it " +
                    "produces cannot be matched back to it.");
            }

            if (!references.Add(instruction.EndToEndId))
            {
                return BankingErrors.Invalid(
                    "payment.endToEndId",
                    $"'{instruction.EndToEndId}' appears twice in one batch, which is how a supplier " +
                    $"gets paid twice.");
            }

            if (instruction.Amount.Currency != currency)
            {
                return BankingErrors.Invalid(
                    "payment.batch",
                    $"Payment '{instruction.EndToEndId}' is in {instruction.Amount.Currency} but the " +
                    $"batch is in {currency}. One batch, one currency.");
            }

            if (!instruction.Amount.IsPositive)
            {
                return BankingErrors.Invalid(
                    "payment.amount",
                    $"Payment '{instruction.EndToEndId}' is {instruction.Amount}. A payment file " +
                    $"moves money out; a refund is a collection, not a negative payment.");
            }

            if (!instruction.Amount.IsInWholeMinorUnits)
            {
                return BankingErrors.Invalid(
                    "payment.amount",
                    $"Payment '{instruction.EndToEndId}' is {instruction.Amount}, which is not a whole " +
                    $"number of minor units and therefore cannot be paid.");
            }
        }

        Money total = Money.Sum(instructions.Select(instruction => instruction.Amount), currency);

        return Result.Success(new PaymentInstructionBatch(
            company,
            debtor,
            requestedExecutionDate,
            instructions,
            total));
    }
}

/// <summary>A rendered payment file.</summary>
/// <param name="FormatId">The format that produced it.</param>
/// <param name="FileName">A suggested file name.</param>
/// <param name="MediaType">The IANA media type of <paramref name="Content"/>.</param>
/// <param name="Content">The bytes.</param>
public sealed record PaymentFile(
    string FormatId,
    string FileName,
    string MediaType,
    ReadOnlyMemory<byte> Content);
