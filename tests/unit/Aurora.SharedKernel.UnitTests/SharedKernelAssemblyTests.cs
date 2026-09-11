using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Shouldly;
using Xunit;

namespace Aurora.SharedKernel.UnitTests;

/// <summary>
/// The two properties of the kernel assembly itself that B-03 has to hold: no floating-point
/// arithmetic anywhere in it, and nothing referenced but the BCL.
/// </summary>
/// <remarks>
/// <para>
/// These are the kernel halves of fitness rules F1 and L1 (`testing-strategy.md` §5). B-04 builds
/// the authoritative, solution-wide versions over every domain assembly; these exist so that B-03
/// is verifiable on its own branch rather than on trust until B-04 lands, and so that a change to
/// the kernel fails in the kernel's own test run.
/// </para>
/// <para>
/// Reflection covers private members too, which is the point: <c>double</c> hidden in a private
/// field is still <c>double</c> in a general ledger.
/// </para>
/// </remarks>
public sealed class SharedKernelAssemblyTests
{
    private static readonly Assembly Kernel = typeof(Money).Assembly;

    [Fact]
    public void No_floating_point_type_appears_anywhere_in_the_kernel()
    {
        List<string> offenders = [.. Kernel.GetTypes().SelectMany(FloatingPointUsesIn)];

        offenders.ShouldBeEmpty(
            "decimal is exact in base 10 and double is not, so money and quantities never touch " +
            "floating point (ADR-0021, fitness rule F1). Offending members: " +
            string.Join("; ", offenders));
    }

    [Fact]
    public void The_kernel_references_nothing_but_the_BCL()
    {
        List<string> offenders =
        [
            .. Kernel.GetReferencedAssemblies()
                .Select(reference => reference.Name ?? "<unnamed>")
                .Where(name => !IsBcl(name)),
        ];

        offenders.ShouldBeEmpty(
            "the kernel is tier 0: every module depends on it, so it may depend on nothing but the " +
            "runtime (modules.md §3, fitness rule L1). Offending references: " +
            string.Join("; ", offenders));
    }

    private static bool IsBcl(string assemblyName) =>
        assemblyName is "netstandard" or "mscorlib" or "System"
        || assemblyName.StartsWith("System.", StringComparison.Ordinal);

    private static IEnumerable<string> FloatingPointUsesIn(Type type)
    {
        // DeclaredOnly, because inherited members belong to whoever declared them: every enum
        // inherits System.Enum's IConvertible.ToDouble, and that is the BCL's business, not ours.
        const BindingFlags Everything =
            BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.DeclaredOnly;

        foreach (FieldInfo field in type.GetFields(Everything))
        {
            if (IsFloatingPoint(field.FieldType))
            {
                yield return $"{type.Name}.{field.Name} (field)";
            }
        }

        foreach (PropertyInfo property in type.GetProperties(Everything))
        {
            if (IsFloatingPoint(property.PropertyType))
            {
                yield return $"{type.Name}.{property.Name} (property)";
            }
        }

        foreach (MethodBase method in type.GetMethods(Everything).Concat<MethodBase>(type.GetConstructors(Everything)))
        {
            if (method is MethodInfo { ReturnType: Type returnType } && IsFloatingPoint(returnType))
            {
                yield return $"{type.Name}.{method.Name} (returns)";
            }

            foreach (ParameterInfo parameter in method.GetParameters())
            {
                if (IsFloatingPoint(parameter.ParameterType))
                {
                    yield return $"{type.Name}.{method.Name}({parameter.Name})";
                }
            }
        }
    }

    private static bool IsFloatingPoint(Type type)
    {
        if (type.IsGenericParameter)
        {
            return false;
        }

        if (type.HasElementType)
        {
            return type.GetElementType() is { } element && IsFloatingPoint(element);
        }

        if (type == typeof(double) || type == typeof(float) || type == typeof(Half))
        {
            return true;
        }

        return type.IsGenericType && type.GetGenericArguments().Any(IsFloatingPoint);
    }
}
