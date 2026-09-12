namespace Aurora.SharedKernel;

/// <summary>
/// An entity that belongs to one company of its tenant.
/// </summary>
/// <remarks>
/// <para>
/// ADR-0010 rule 6 scopes data by an EF global query filter on <c>CompanyId</c>, parameterized
/// from the resolved <see cref="CompanyScope"/>. ADR-0029 A1.2 H-2 makes that structural: a
/// context whose model maps any type implementing this interface has one constructor, and it
/// takes a <see cref="CompanyScope"/>, so a filtered context cannot be obtained unfiltered. Model
/// metadata is the mechanism, and this interface is what the metadata reads (B-06.3).
/// </para>
/// <para>
/// <b>A company is scoped to itself.</b> The aggregate that <em>is</em> a company implements this
/// by returning its own id. That keeps the rule uniform — one filter shape for every
/// company-scoped type — and it means <c>Company</c> needs no special case anywhere a scope is
/// applied.
/// </para>
/// <para>
/// Read-only on purpose: an entity does not move between companies under a filter that has
/// already run.
/// </para>
/// </remarks>
public interface ICompanyScoped
{
    /// <summary>The company this entity belongs to. A company belongs to itself.</summary>
    CompanyId CompanyId { get; }
}
