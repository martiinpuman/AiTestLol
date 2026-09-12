using System;
using System.Collections.Generic;

namespace Aurora.SharedKernel;

/// <summary>
/// Which companies of the current tenant a unit of work may see: every one of them, or a named
/// set. There is no third form.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two forms, no third.</b> <see cref="AllCompaniesInTenant"/> is the scope of a user whose
/// Role Assignment names no company (ADR-0010 rule 4): every company in the tenant, including
/// companies created later. <see cref="Of"/> is the scope of a user assigned to named companies.
/// A scope over <em>no</em> company cannot be built — <see cref="Of"/> throws on an empty
/// collection — because the alternative is the one bug this type exists to make impossible: an
/// empty filter list that a query builder turns into no filter at all is <c>WHERE 1=1</c>, which
/// is every company. ADR-0029 §5 states it as "an empty filter list is never 'no filter'"; here
/// the empty list has no constructor.
/// </para>
/// <para>
/// <b>Unassigned ids are refused, not dropped.</b> A default <see cref="CompanyId"/> in the input
/// came from a field nobody set. Dropping it would shrink the scope silently, and shrinking is
/// only "safe" until the dropped id was the one the caller meant, so the whole call is refused.
/// </para>
/// <para>
/// <b>Equality is by the set of ids.</b> <see cref="Of"/> deduplicates and orders, so
/// <c>Of([a, b])</c>, <c>Of([b, a])</c> and <c>Of([a, b, a])</c> are one value with one hash
/// code: safe as a dictionary key, safe to compare across a cache boundary, safe to log.
/// </para>
/// <para>
/// <b>How a query filter reads it.</b> Branch on <see cref="IsAllCompaniesInTenant"/>:
/// <see langword="true"/> emits no predicate, because tenant isolation is the database
/// (ADR-0007); <see langword="false"/> emits <c>CompanyId = ANY(@p)</c> over
/// <see cref="CompanyIds"/>. Filtering after materialisation stays forbidden (ADR-0010 rule 6).
/// The scope never reaches a query by ambient lookup: ADR-0029 A1.2 H-2 makes it a constructor
/// parameter of any context that maps an <c>ICompanyScoped</c> entity, which B-06.3 builds.
/// </para>
/// <para>
/// <b>Why a class and not a struct.</b> A struct has a <see langword="default"/> that C# cannot
/// prevent, and a default scope would have to mean something — either every company, which is
/// catastrophic, or nothing, which is a third form. A sealed class with a private constructor has
/// exactly the two forms its factories produce; the only other value is <see langword="null"/>,
/// which nullable reference types flag at every boundary.
/// </para>
/// </remarks>
public sealed class CompanyScope : IEquatable<CompanyScope>
{
    private static readonly Comparer<CompanyId> ByUuid =
        Comparer<CompanyId>.Create((left, right) => left.Value.CompareTo(right.Value));

    /// <summary>Distinct and ascending by UUID. Empty only for the all-companies form.</summary>
    private readonly CompanyId[] _companyIds;

    private CompanyScope(CompanyId[] companyIds)
    {
        _companyIds = companyIds;
        CompanyIds = Array.AsReadOnly(companyIds);
    }

    /// <summary>
    /// Every company in the tenant, including ones created after this scope was resolved. Emits
    /// no company predicate: the tenant database is the boundary.
    /// </summary>
    public static CompanyScope AllCompaniesInTenant { get; } = new([]);

    /// <summary>
    /// Whether this scope is <see cref="AllCompaniesInTenant"/>. A query filter branches on this,
    /// never on the count of <see cref="CompanyIds"/>.
    /// </summary>
    public bool IsAllCompaniesInTenant => _companyIds.Length == 0;

    /// <summary>
    /// The companies this scope names, each once, in ascending order of their UUIDs.
    /// </summary>
    /// <remarks>
    /// Empty exactly when <see cref="IsAllCompaniesInTenant"/>, because the empty set has no other
    /// way in. That is a fact about this type, not a convention for a consumer to lean on: a
    /// consumer reads <see cref="IsAllCompaniesInTenant"/>, so that "no ids" never reads as "no
    /// filter" anywhere a reviewer might see it.
    /// </remarks>
    public IReadOnlyList<CompanyId> CompanyIds { get; }

    /// <summary>
    /// A scope over the named companies, deduplicated and ordered so that two scopes over the same
    /// companies are equal however they were listed.
    /// </summary>
    /// <param name="companyIds">
    /// The companies. Read once, so a one-shot sequence is acceptable; copied, so the caller may
    /// change the source afterwards without changing the scope.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="companyIds"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="companyIds"/> is empty, or any element is an unassigned (default)
    /// <see cref="CompanyId"/>. Both are refused whole rather than repaired, for the reasons in the
    /// type's remarks.
    /// </exception>
    public static CompanyScope Of(IEnumerable<CompanyId> companyIds)
    {
        ArgumentNullException.ThrowIfNull(companyIds);

        SortedSet<CompanyId> distinct = new(ByUuid);
        foreach (CompanyId companyId in companyIds)
        {
            if (companyId.IsEmpty)
            {
                throw new ArgumentException(
                    "A CompanyScope cannot include an unassigned CompanyId (the struct default). " +
                    "It comes from a field nobody set, and dropping it silently would shrink the " +
                    "scope without anyone noticing, so the whole scope is refused.",
                    nameof(companyIds));
            }

            distinct.Add(companyId);
        }

        if (distinct.Count == 0)
        {
            throw new ArgumentException(
                "A CompanyScope over no companies cannot be built. An empty set is not 'every " +
                "company': use CompanyScope.AllCompaniesInTenant to mean that, or refuse the " +
                "caller (ADR-0029 §5). An empty filter list must never decay into no filter.",
                nameof(companyIds));
        }

        CompanyId[] ordered = new CompanyId[distinct.Count];
        distinct.CopyTo(ordered);
        return new CompanyScope(ordered);
    }

    /// <inheritdoc/>
    public bool Equals(CompanyScope? other)
    {
        if (other is null)
        {
            return false;
        }

        return ReferenceEquals(this, other) || _companyIds.AsSpan().SequenceEqual(other._companyIds);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as CompanyScope);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        HashCode hash = default;
        foreach (CompanyId companyId in _companyIds)
        {
            hash.Add(companyId);
        }

        return hash.ToHashCode();
    }

    /// <summary>Value equality: the same form over the same companies.</summary>
    public static bool operator ==(CompanyScope? left, CompanyScope? right) =>
        left is null ? right is null : left.Equals(right);

    /// <summary>The negation of <see cref="operator =="/>.</summary>
    public static bool operator !=(CompanyScope? left, CompanyScope? right) => !(left == right);

    /// <summary>
    /// Names the form and, for a named set, lists the UUIDs in their canonical order. Culture-
    /// invariant and never localized: this is for logs and assertion messages, not for a screen.
    /// </summary>
    public override string ToString() => IsAllCompaniesInTenant
        ? "every company in the tenant"
        : "companies " + string.Join(", ", _companyIds);
}
