using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aurora.Countries.Contracts.Accounting;
using Aurora.Countries.Contracts.Localization;
using Aurora.Countries.Contracts.Taxation;
using Aurora.SharedKernel;

namespace Aurora.Countries.Contracts.Interchange;

/// <summary>
/// Extension point 6 — reading and writing a jurisdiction's accounting-data interchange format
/// (ADR-0008 §7).
/// </summary>
/// <remarks>
/// <para>
/// The research found this to be the seam no incumbent models as a discrete, versioned unit, which
/// is itself the argument for modelling it: an accountant changing systems, and a tenant leaving
/// (ADR-0007 §11.4), both need the ledger out in a form somebody else can read.
/// </para>
/// <para>
/// A package is handed a <see cref="IInterchangeSource"/> to pull from and a <see cref="Stream"/> to
/// write to — never a database connection, never a <c>DbContext</c>, never a tenant. It cannot reach
/// a tenant the caller did not open, cannot widen the period it was given, and cannot read a table
/// core did not offer. That is the shape every extension point that touches tenant data takes.
/// </para>
/// </remarks>
[ExtensionPoint(CountryPackageCapability.InterchangeFormat)]
public interface IInterchangeFormat
{
    /// <summary>A stable id for this format, such as a published schema version.</summary>
    string FormatId { get; }

    /// <summary>The format's name as a user choosing an export sees it.</summary>
    LocalizedText Name { get; }

    /// <summary>
    /// Writes the accounting data for <paramref name="period"/> into
    /// <paramref name="destination"/>, and reports how much it wrote.
    /// </summary>
    /// <remarks>
    /// The count comes back deliberately. An exporter that returns nothing but success cannot tell
    /// an empty period from a broken query, and neither can the test that covers it.
    /// </remarks>
    Task<Result<InterchangeCounts>> ExportAsync(
        IInterchangeSource source,
        Stream destination,
        DateRange period,
        CancellationToken cancellationToken);

    /// <summary>Reads accounting data from <paramref name="source"/> into core, and reports how much.</summary>
    Task<Result<InterchangeCounts>> ImportAsync(
        Stream source,
        IInterchangeSink sink,
        CancellationToken cancellationToken);
}

/// <summary>What core lets an interchange format read. Pull-only, and scoped by the caller.</summary>
public interface IInterchangeSource
{
    /// <summary>The company being exported.</summary>
    CompanyId Company { get; }

    /// <summary>The currency its ledger is kept in.</summary>
    Currency LedgerCurrency { get; }

    /// <summary>The chart of accounts, streamed.</summary>
    IAsyncEnumerable<AccountRecord> ReadAccountsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The journal entries in <paramref name="period"/>, streamed oldest first.
    /// </summary>
    /// <remarks>
    /// Streamed rather than returned as a list because a year of a real ledger does not fit in
    /// memory, and an exporter that materialised it would work in every test and fail on the first
    /// tenant with volume.
    /// </remarks>
    IAsyncEnumerable<JournalEntryRecord> ReadJournalEntriesAsync(
        DateRange period,
        CancellationToken cancellationToken);
}

/// <summary>What core lets an interchange format write. Each write is validated by core.</summary>
public interface IInterchangeSink
{
    /// <summary>Offers an account to core.</summary>
    Task<Result> WriteAccountAsync(AccountRecord account, CancellationToken cancellationToken);

    /// <summary>Offers a journal entry to core.</summary>
    Task<Result> WriteJournalEntryAsync(JournalEntryRecord entry, CancellationToken cancellationToken);
}

/// <summary>One account, in interchange terms.</summary>
/// <param name="Code">Its code.</param>
/// <param name="Name">Its name.</param>
/// <param name="Kind">Where it sits in the accounting equation.</param>
public sealed record AccountRecord(AccountCode Code, LocalizedText Name, AccountKind Kind);

/// <summary>One line of a journal entry, in interchange terms.</summary>
/// <param name="Account">The account posted to.</param>
/// <param name="Debit">The debit, zero if this is a credit line.</param>
/// <param name="Credit">The credit, zero if this is a debit line.</param>
/// <param name="TaxCode">The tax code carried, where the line carries one.</param>
public sealed record JournalLineRecord(
    AccountCode Account,
    Money Debit,
    Money Credit,
    TaxCode? TaxCode);

/// <summary>One journal entry, in interchange terms.</summary>
/// <remarks>
/// Built through <see cref="Create"/> because an entry that does not balance is not an entry. An
/// import that accepted one would put the difference into a ledger that is supposed to be the store
/// of record, and the discrepancy would surface as an unexplained suspense balance months later.
/// </remarks>
public sealed class JournalEntryRecord
{
    private JournalEntryRecord(
        string entryId,
        DateOnly entryDate,
        LocalizedText? description,
        IReadOnlyList<JournalLineRecord> lines,
        Money total)
    {
        EntryId = entryId;
        EntryDate = entryDate;
        Description = description;
        Lines = lines;
        Total = total;
    }

    /// <summary>The entry's identifier in the system that produced it.</summary>
    public string EntryId { get; }

    /// <summary>The date it is posted on.</summary>
    public DateOnly EntryDate { get; }

    /// <summary>What it is for, if the source says.</summary>
    public LocalizedText? Description { get; }

    /// <summary>Its lines.</summary>
    public IReadOnlyList<JournalLineRecord> Lines { get; }

    /// <summary>The entry's total debits, which equal its total credits.</summary>
    public Money Total { get; }

    /// <summary>Builds an entry, refusing one that does not balance.</summary>
    public static Result<JournalEntryRecord> Create(
        string entryId,
        DateOnly entryDate,
        LocalizedText? description,
        IReadOnlyList<JournalLineRecord> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (string.IsNullOrWhiteSpace(entryId))
        {
            return InterchangeErrors.Invalid("entry.id", "A journal entry needs an identifier.");
        }

        if (lines.Count == 0)
        {
            return InterchangeErrors.Invalid("entry.lines", "A journal entry with no lines posts nothing.");
        }

        Currency currency = lines[0].Debit.Currency;
        foreach (JournalLineRecord line in lines)
        {
            if (line.Debit.Currency != currency || line.Credit.Currency != currency)
            {
                return InterchangeErrors.Invalid(
                    "entry.lines",
                    $"Entry '{entryId}' mixes currencies. One entry, one currency.");
            }

            if (line.Debit.IsNegative || line.Credit.IsNegative)
            {
                return InterchangeErrors.Invalid(
                    "entry.lines",
                    $"Entry '{entryId}' has a negative debit or credit. A reversal is a posting on the " +
                    $"other side, not a negative one.");
            }

            if (!line.Debit.IsZero && !line.Credit.IsZero)
            {
                return InterchangeErrors.Invalid(
                    "entry.lines",
                    $"Entry '{entryId}' has a line that is both a debit and a credit.");
            }
        }

        Money debits = Money.Sum(lines.Select(line => line.Debit), currency);
        Money credits = Money.Sum(lines.Select(line => line.Credit), currency);

        if (debits != credits)
        {
            return InterchangeErrors.Invalid(
                "entry.lines",
                $"Entry '{entryId}' debits {debits} and credits {credits}. An entry that does not " +
                $"balance is not an entry.");
        }

        return Result.Success(new JournalEntryRecord(entryId, entryDate, description, lines, debits));
    }
}

/// <summary>
/// How much an interchange run moved. Reported on every path, so a run that moved nothing says so.
/// </summary>
/// <param name="Accounts">Accounts written.</param>
/// <param name="JournalEntries">Entries written.</param>
/// <param name="JournalLines">Lines written.</param>
public readonly record struct InterchangeCounts(int Accounts, int JournalEntries, int JournalLines)
{
    /// <summary>Whether the run moved nothing at all.</summary>
    public bool IsEmpty => Accounts == 0 && JournalEntries == 0 && JournalLines == 0;

    /// <inheritdoc/>
    public override string ToString() =>
        $"{Accounts} account(s), {JournalEntries} entry/entries, {JournalLines} line(s)";
}

/// <summary>The errors an interchange format produces.</summary>
public static class InterchangeErrors
{
    /// <summary>Data offered to or by an interchange format is not usable.</summary>
    public const string InvalidCode = "country_package.interchange.invalid";

    /// <summary>Data offered to or by an interchange format is not usable.</summary>
    public static Error Invalid(string field, string description) =>
        Error.Rejected(InvalidCode, $"{field}: {description}");
}
