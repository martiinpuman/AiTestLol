using System;
using System.Collections.Generic;
using System.Linq;
using Aurora.SharedKernel;

namespace Aurora.Countries.Contracts.Banking;

/// <summary>One line of a bank statement.</summary>
/// <param name="BookingDate">When the bank booked it.</param>
/// <param name="ValueDate">When it counts for interest — often, but not always, the same day.</param>
/// <param name="Amount">Signed: negative out, positive in.</param>
/// <param name="Reference">
/// The end-to-end reference, where the bank returns one. This is what reconciles the line against
/// the <see cref="PaymentInstruction"/> that caused it.
/// </param>
/// <param name="CounterpartyName">Who the money moved to or from, as the bank reports it.</param>
/// <param name="RemittanceInformation">What the counterparty said the payment was for.</param>
public sealed record BankStatementLine(
    DateOnly BookingDate,
    DateOnly ValueDate,
    Money Amount,
    string? Reference,
    string? CounterpartyName,
    string? RemittanceInformation);

/// <summary>
/// A parsed bank statement, in the neutral shape core reconciles against.
/// </summary>
/// <remarks>
/// A parser that quietly drops a line it did not understand produces a statement that looks fine and
/// reconciles wrong. <see cref="Create"/> therefore refuses any statement whose lines do not carry
/// the opening balance to the closing balance — the one arithmetic check a statement can be held to,
/// and the one that catches a dropped line, a sign flip and a mis-parsed amount alike.
/// </remarks>
public sealed class BankStatement
{
    private BankStatement(
        BankAccountReference account,
        DateRange period,
        Money openingBalance,
        Money closingBalance,
        IReadOnlyList<BankStatementLine> lines)
    {
        Account = account;
        Period = period;
        OpeningBalance = openingBalance;
        ClosingBalance = closingBalance;
        Lines = lines;
    }

    /// <summary>The account the statement is for.</summary>
    public BankAccountReference Account { get; }

    /// <summary>The period it covers.</summary>
    public DateRange Period { get; }

    /// <summary>The balance it opens with.</summary>
    public Money OpeningBalance { get; }

    /// <summary>The balance it closes with.</summary>
    public Money ClosingBalance { get; }

    /// <summary>The lines, in statement order.</summary>
    public IReadOnlyList<BankStatementLine> Lines { get; }

    /// <summary>How many lines were parsed — the number a parser's own test can assert on.</summary>
    public int LineCount => Lines.Count;

    /// <summary>
    /// Builds a statement, refusing one whose lines do not add up or whose currencies disagree.
    /// </summary>
    public static Result<BankStatement> Create(
        BankAccountReference account,
        DateRange period,
        Money openingBalance,
        Money closingBalance,
        IReadOnlyList<BankStatementLine> lines)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(lines);

        if (period.IsEmpty)
        {
            return BankingErrors.Invalid("statement.period", "A statement covering no day covers nothing.");
        }

        Currency currency = openingBalance.Currency;

        if (closingBalance.Currency != currency)
        {
            return BankingErrors.Invalid(
                "statement.balances",
                $"The statement opens in {currency} and closes in {closingBalance.Currency}.");
        }

        foreach (BankStatementLine line in lines)
        {
            if (line.Amount.Currency != currency)
            {
                return BankingErrors.Invalid(
                    "statement.lines",
                    $"A line on {line.BookingDate:O} is in {line.Amount.Currency} but the statement is " +
                    $"in {currency}.");
            }

            if (!period.Contains(line.BookingDate))
            {
                return BankingErrors.Invalid(
                    "statement.lines",
                    $"A line is booked on {line.BookingDate:O}, outside the statement period {period}.");
            }
        }

        Money movement = Money.Sum(lines.Select(line => line.Amount), currency);
        Money expected = openingBalance + movement;

        if (expected != closingBalance)
        {
            return BankingErrors.Invalid(
                "statement.balances",
                $"The lines move {movement}, which takes {openingBalance} to {expected}, but the " +
                $"statement closes at {closingBalance}. A statement whose lines do not carry its " +
                $"opening balance to its closing balance has lost or gained a line somewhere, and " +
                $"reconciling against it would put the difference somewhere nobody looks.");
        }

        return Result.Success(new BankStatement(account, period, openingBalance, closingBalance, lines));
    }
}

/// <summary>The errors a bank file adapter produces.</summary>
public static class BankingErrors
{
    /// <summary>A payment batch or a parsed statement is not usable.</summary>
    public const string InvalidCode = "country_package.banking.invalid";

    /// <summary>A payment batch or a parsed statement is not usable.</summary>
    public static Error Invalid(string field, string description) =>
        Error.Rejected(InvalidCode, $"{field}: {description}");
}
