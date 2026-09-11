using System;

namespace Aurora.SharedKernel;

/// <summary>
/// Thrown when two monetary amounts in different currencies are combined or compared.
/// </summary>
/// <remarks>
/// ADR-0021 §1: arithmetic between different currencies throws. There is no implicit conversion
/// and no "default currency" — converting between currencies needs a rate, a rate source and a
/// date, all of which are document data, so the only honest thing a value object can do with
/// NZD + AUD is refuse. This is a distinct type rather than a bare
/// <see cref="InvalidOperationException"/> so the failure is unambiguous in a log and assertable
/// in a test.
/// </remarks>
public sealed class CurrencyMismatchException : InvalidOperationException
{
    /// <summary>Creates the exception for a pair of currencies that do not match.</summary>
    public CurrencyMismatchException(Currency left, Currency right)
        : base($"Amounts in different currencies cannot be combined or compared: {left} and {right}. " +
               "Convert one of them explicitly, with a rate, a rate source and a date.")
    {
        Left = left;
        Right = right;
    }

    /// <summary>The currency on the left of the refused operation.</summary>
    public Currency Left { get; }

    /// <summary>The currency on the right of the refused operation.</summary>
    public Currency Right { get; }
}
