using System;
using System.Text.RegularExpressions;
using Aurora.SharedKernel;

namespace Aurora.Countries.Contracts.Taxation;

/// <summary>
/// A jurisdiction's own name for a tax treatment — <c>GST15</c>, <c>ZERO</c>, <c>EXEMPT</c>.
/// </summary>
/// <remarks>
/// Core never interprets a tax code. It carries one on a document line, hands it back to the
/// package with a date, and receives a rule. Anything else — core knowing that <c>GST15</c> means
/// fifteen percent — would be the jurisdiction branch this contract exists to keep out.
/// </remarks>
public readonly partial record struct TaxCode
{
    private const int MaxLength = 32;

    private readonly string? _value;

    private TaxCode(string value) => _value = value;

    /// <summary>The code itself.</summary>
    /// <exception cref="InvalidOperationException">This is the default, unassigned value.</exception>
    public string Value => _value ?? throw Unassigned();

    /// <summary>Whether this value names a tax code at all, rather than being <c>default</c>.</summary>
    public bool IsSpecified => _value is not null;

    /// <summary>Reads a tax code, rejecting whitespace and empty text.</summary>
    public static Result<TaxCode> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return TaxErrors.Invalid("taxCode", "A tax code is required.");
        }

        if (value.Length > MaxLength)
        {
            return TaxErrors.Invalid(
                "taxCode",
                $"A tax code may be at most {MaxLength} characters; '{value}' is {value.Length}.");
        }

        return Shape().IsMatch(value)
            ? Result.Success(new TaxCode(value))
            : TaxErrors.Invalid(
                "taxCode",
                $"'{value}' is not a tax code. Expected letters, digits, dots, dashes or underscores " +
                $"with no spaces.");
    }

    /// <inheritdoc/>
    public override string ToString() => _value ?? "<unspecified tax code>";

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._\-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();

    private static InvalidOperationException Unassigned() =>
        new("This TaxCode names no tax. It is the default value of the struct, which only exists " +
            "because C# gives every struct one; build codes with TaxCode.Create.");
}
