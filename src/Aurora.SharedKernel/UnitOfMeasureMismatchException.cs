using System;

namespace Aurora.SharedKernel;

/// <summary>
/// Thrown when two quantities in different units of measure are combined or compared.
/// </summary>
/// <remarks>
/// Ten pieces plus two kilograms is not twelve of anything. Converting between units needs a
/// conversion factor, which is a property of the item being counted, so the only honest thing a
/// value object can do with a mixed pair is refuse. This is a distinct type rather than a bare
/// <see cref="InvalidOperationException"/> so the failure is unambiguous in a log and assertable
/// in a test — the same reasoning as <see cref="CurrencyMismatchException"/>.
/// </remarks>
public sealed class UnitOfMeasureMismatchException : InvalidOperationException
{
    /// <summary>Creates the exception for a pair of units that do not match.</summary>
    public UnitOfMeasureMismatchException(UnitOfMeasure left, UnitOfMeasure right)
        : base($"Quantities in different units of measure cannot be combined or compared: {left} " +
               $"and {right}. Convert one of them explicitly, with the item's conversion factor.")
    {
        Left = left;
        Right = right;
    }

    /// <summary>The unit on the left of the refused operation.</summary>
    public UnitOfMeasure Left { get; }

    /// <summary>The unit on the right of the refused operation.</summary>
    public UnitOfMeasure Right { get; }
}
