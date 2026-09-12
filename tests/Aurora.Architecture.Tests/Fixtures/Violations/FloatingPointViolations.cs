using System;

namespace Aurora.Architecture.Tests.Fixtures.Violations;

/// <summary>
/// The exact defect <c>docs/reviews/B-03.md</c> m-1 planted in the kernel and watched a
/// signature-only rule ignore: a monetary <c>decimal</c> multiplied through <c>double</c>, with
/// nothing floating-point anywhere in the signature.
/// </summary>
/// <remarks>
/// <b>Deliberately violating fixture. Never referenced by production code.</b> Its whole purpose
/// is to be found by <see cref="Rules.FloatingPointRule"/>. The rule must report it at
/// <see cref="Rules.ViolationSite.Local"/> or <see cref="Rules.ViolationSite.MemberReference"/> -
/// inside the body - because a rule that only reported the signature would report nothing here.
/// </remarks>
internal static class MoneyMathHiddenInsideAMethodBody
{
    /// <summary>decimal in, decimal out, double in the middle. This is the one that got through.</summary>
    public static decimal DiscountedTotal(decimal amount, int percentOff)
    {
        double factor = 1.0 - (percentOff / 100.0);
        return (decimal)((double)amount * factor);
    }

    /// <summary>
    /// The same violation with no named local at all: the cast is the only evidence, and it
    /// survives as a call to <c>System.Decimal::op_Explicit</c> returning <c>float64</c>.
    /// </summary>
    public static decimal HalfOf(decimal amount) => (decimal)((double)amount / 2.0);

    /// <summary>A floating-point value borrowed from the BCL, never stored in a local of ours.</summary>
    public static decimal SquareRootOf(decimal amount) => (decimal)Math.Sqrt((double)amount);
}

/// <summary>
/// The signature-level violations a reflection-based rule does catch, kept so the rule is proven
/// on both halves rather than only on the half that used to be missing.
/// </summary>
/// <remarks><b>Deliberately violating fixture. Never referenced by production code.</b></remarks>
internal sealed class RateCarriedAsDouble
{
    private readonly double _rate;

    public RateCarriedAsDouble(double rate) => _rate = rate;

    public double Rate => _rate;

    public float AsSingle() => (float)_rate;
}
