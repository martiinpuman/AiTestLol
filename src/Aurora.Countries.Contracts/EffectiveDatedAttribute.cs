using System;

namespace Aurora.Countries.Contracts;

/// <summary>
/// Marks a type as a rule that was true over a stated period and may not be true now
/// (ADR-0008 §6.2).
/// </summary>
/// <remarks>
/// <para>
/// Every extension point that resolves one of these must be handed the business date to resolve it
/// as of — never <c>now</c>. Re-printing a two-year-old invoice has to reproduce the rate that
/// applied then, and the way that breaks is a convenience overload without a date, added in good
/// faith, three modules away.
/// </para>
/// <para>
/// The marker exists so that rule is <b>checked</b> rather than remembered:
/// <c>EffectiveDatingTests</c> reflects over this assembly and fails on any method that returns a
/// single marked value without taking a <see cref="DateOnly"/>. A method returning the whole
/// history of a rule is a different question and is allowed to have no date.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class EffectiveDatedAttribute : Attribute
{
}
