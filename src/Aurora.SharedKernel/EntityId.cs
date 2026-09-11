using System;

namespace Aurora.SharedKernel;

/// <summary>
/// The behaviour every <see cref="IEntityId{TSelf}"/> shares, in one place.
/// </summary>
/// <remarks>
/// An identifier type is deliberately a handful of lines of delegation to this class and no logic
/// of its own. That is what keeps fifty of them across the system from drifting into fifty
/// slightly different opinions about what an empty identifier means or which exception a bad route
/// segment raises.
/// </remarks>
public static class EntityId
{
    /// <summary>
    /// Checks that a <see cref="Guid"/> actually identifies something.
    /// </summary>
    /// <remarks>
    /// All zeros is refused because it is not a value anybody minted: it is an unassigned field
    /// that reached a constructor, and letting it through is how <c>00000000-0000-0000-0000-000000000000</c>
    /// ends up in a foreign key and joins to nothing for three years. The struct default is still
    /// reachable — C# cannot prevent that — which is why <see cref="IEntityId{TSelf}.IsEmpty"/>
    /// exists to detect it.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="value"/> is all zeros.</exception>
    public static Guid Assigned(Guid value, string idTypeName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                $"An all-zero Guid does not identify anything, so it is not a {idTypeName}. It " +
                "comes from an unassigned field rather than from Create() or from a stored value.",
                nameof(value));
        }

        return value;
    }

    /// <summary>
    /// Reads an identifier from its text form, the inverse of <see cref="object.ToString"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">
    /// <paramref name="text"/> is not a UUID, or is the all-zero UUID.
    /// </exception>
    public static TId Parse<TId>(string text, string idTypeName)
        where TId : struct, IEntityId<TId>
    {
        ArgumentNullException.ThrowIfNull(text);

        return TryParse<TId>(text, out TId parsed)
            ? parsed
            : throw new FormatException($"'{text}' is not a {idTypeName}.");
    }

    /// <summary>
    /// Reads an identifier from its text form, reporting failure rather than throwing — the shape
    /// a route or query-string binder wants.
    /// </summary>
    public static bool TryParse<TId>(string? text, out TId result)
        where TId : struct, IEntityId<TId>
    {
        if (Guid.TryParse(text, out Guid value) && value != Guid.Empty)
        {
            result = TId.From(value);
            return true;
        }

        result = default;
        return false;
    }
}
