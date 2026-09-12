namespace Aurora.Platform.Tenancy.Contracts;

/// <summary>
/// Which half of the expand/contract discipline a migration is (ADR-0007 §7.2, ADR-0003 rule 6).
/// </summary>
/// <remarks>
/// Every EF migration carries exactly one of these through <see cref="MigrationSafetyAttribute"/>.
/// The migration safety rules in <c>tests/Aurora.Architecture.Tests</c> generate each migration's
/// SQL and hold it to the category it declares, so the category is a claim the build checks, not
/// a comment.
/// </remarks>
public enum MigrationCategory
{
    /// <summary>
    /// Adds schema and takes nothing away: new tables, nullable or defaulted columns, indexes.
    /// Code version N-1 keeps running against the result, which is what lets the schema roll out
    /// across every tenant ahead of the code that uses it (ADR-0007 §7.5).
    /// </summary>
    Expand,

    /// <summary>
    /// Removes or narrows what an earlier <see cref="Expand"/> made redundant: drops, renames,
    /// type changes, <c>NOT NULL</c>. Names that Expand through
    /// <see cref="MigrationSafetyAttribute.Contracts"/> and ships at least one release after it.
    /// </summary>
    Contract,

    /// <summary>
    /// Moves data and touches no schema: <c>INSERT</c> and <c>UPDATE</c> only. No DDL of any kind,
    /// and nothing that deletes.
    /// </summary>
    DataOnly,
}
