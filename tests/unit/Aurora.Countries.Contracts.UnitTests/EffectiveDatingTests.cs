using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Aurora.Countries.Contracts.Taxation;
using Aurora.SharedKernel;
using Shouldly;
using Xunit;

namespace Aurora.Countries.Contracts.UnitTests;

/// <summary>
/// ADR-0008 §6.2: a rule is resolved as of a business date, never as of now. This is that rule made
/// mechanical.
/// </summary>
/// <remarks>
/// The thing that breaks effective dating is not a missing feature, it is a convenience overload
/// added in good faith three modules away — <c>Resolve(taxCode)</c> next to
/// <c>Resolve(taxCode, asOf)</c>. Re-printing a two-year-old invoice then reprices it at today's
/// rate. So the contract is scanned for exactly that shape, and the scan is proved to be capable of
/// finding one.
/// </remarks>
public sealed class EffectiveDatingTests
{
    [Fact]
    public void No_extension_point_interface_resolves_a_dated_rule_without_a_date()
    {
        List<string> violations =
            DateBlindResolvers(typeof(CoreContract).Assembly.GetExportedTypes());

        violations.ShouldBeEmpty(
            "every interface method returning an [EffectiveDated] value must take the business date " +
            "to resolve it as of");

        // The scan is over interfaces, so it has to have seen some.
        typeof(CoreContract).Assembly.GetExportedTypes().Count(type => type.IsInterface)
            .ShouldBeGreaterThan(10);
    }

    /// <summary>
    /// The scan, run over a type written to break the rule. Without this, a scan that silently
    /// matched nothing — a renamed attribute, a reflection flag that misses interface methods —
    /// would report the contract clean forever.
    /// </summary>
    [Fact]
    public void The_scan_finds_a_convenience_overload_when_there_is_one()
    {
        List<string> violations = DateBlindResolvers([typeof(ConvenienceOverloadFixture)]);

        violations.Count.ShouldBe(1);
        violations[0].ShouldContain(nameof(ConvenienceOverloadFixture.ResolveToday));
    }

    /// <summary>
    /// Returning the whole history of a rule needs no date — the question is not "what applies", it
    /// is "what has ever applied" — and the scan must not report it.
    /// </summary>
    [Fact]
    public void Returning_a_whole_history_is_not_a_violation()
    {
        DateBlindResolvers([typeof(ConvenienceOverloadFixture)])
            .ShouldNotContain(violation =>
                violation.Contains(nameof(ConvenienceOverloadFixture.History), StringComparison.Ordinal));
    }

    /// <summary>
    /// Something is actually marked, so the scan above is not passing over an empty set.
    /// </summary>
    [Fact]
    public void The_contract_marks_the_values_that_are_effective_dated()
    {
        Type[] dated =
        [
            .. typeof(CoreContract).Assembly.GetExportedTypes()
                .Where(type => type.GetCustomAttribute<EffectiveDatedAttribute>() is not null),
        ];

        dated.ShouldContain(typeof(TaxRuleVersion));
        dated.Length.ShouldBeGreaterThanOrEqualTo(3);
    }

    /// <summary>
    /// Every method on an <b>interface</b> in <paramref name="types"/> that resolves a single
    /// <see cref="EffectiveDatedAttribute"/>-marked value without taking a
    /// <see cref="DateOnly"/>.
    /// </summary>
    /// <remarks>
    /// Interfaces only, and the name of the test says so. An interface is what a package implements
    /// and core calls, so it is where a date-blind overload would actually be used. The factories and
    /// constructors on the dated values themselves take the validity period as an argument and
    /// legitimately do not take an as-of date: scanning them would report a violation that is not one,
    /// and a rule that cries wolf gets deleted.
    /// </remarks>
    private static List<string> DateBlindResolvers(IEnumerable<Type> types)
    {
        List<string> violations = [];

        foreach (Type type in types.Where(candidate => candidate.IsInterface))
        {
            foreach (MethodInfo method in type.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                Type? resolved = SingleEffectiveDatedResult(method.ReturnType);
                if (resolved is null)
                {
                    continue;
                }

                if (method.GetParameters().Any(parameter => parameter.ParameterType == typeof(DateOnly)))
                {
                    continue;
                }

                violations.Add($"{type.Name}.{method.Name} returns {resolved.Name} without a DateOnly");
            }
        }

        return violations;
    }

    /// <summary>
    /// The marked type a return value resolves to — unwrapping <see cref="Result{TValue}"/> and
    /// <see cref="Nullable{T}"/>, and answering <see langword="null"/> for a collection, which is a
    /// history rather than a resolution.
    /// </summary>
    private static Type? SingleEffectiveDatedResult(Type returnType)
    {
        Type candidate = returnType;

        if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(Result<>))
        {
            candidate = candidate.GetGenericArguments()[0];
        }

        if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            candidate = candidate.GetGenericArguments()[0];
        }

        if (candidate != typeof(string) && typeof(IEnumerable).IsAssignableFrom(candidate))
        {
            return null;
        }

        return candidate.GetCustomAttribute<EffectiveDatedAttribute>() is null ? null : candidate;
    }
}

/// <summary>
/// A contract written the way an effective-dated one must not be, so the scan above has something
/// to find.
/// </summary>
/// <remarks>
/// It is not registered as an extension point and nothing loads it. It exists to be caught.
/// </remarks>
internal interface ConvenienceOverloadFixture
{
    /// <summary>The shape the rule forbids: a resolution with no business date.</summary>
    Result<TaxRuleVersion> ResolveToday(TaxCode code);

    /// <summary>The shape the rule allows, for comparison.</summary>
    Result<TaxRuleVersion> Resolve(TaxCode code, DateOnly asOf);

    /// <summary>A history, which legitimately needs no date.</summary>
    IReadOnlyList<TaxRuleVersion> History(TaxCode code);
}
