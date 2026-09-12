using System;

namespace Aurora.Platform.Tenancy.Contracts;

/// <summary>
/// The expand/contract category of one EF migration, and why it is that category (ADR-0007 §7.2).
/// </summary>
/// <remarks>
/// <para>
/// Mandatory on every migration class in every production assembly - the fitness rule <b>MIG1</b>
/// fails the build on one without it. The category and the reason are constructor parameters
/// rather than settable properties so that a migration cannot compile with either missing.
/// </para>
/// <para>
/// A <see cref="MigrationCategory.Contract"/> names the Expand it contracts in
/// <see cref="Contracts"/>, by the migration id EF assigns (<c>20260911172124_InitialCatalog</c>).
/// MIG1 checks today that the link names an existing Expand; the release gate (<b>MIG3</b>, B-09.3,
/// not yet built) will read it and refuse a contract shipping in the same release as its expand.
/// An Expand or DataOnly migration leaves it unset.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class MigrationSafetyAttribute : Attribute
{
    public MigrationSafetyAttribute(MigrationCategory category, string reason)
    {
        Category = category;
        Reason = reason;
    }

    /// <summary>Which half of expand/contract this migration is.</summary>
    public MigrationCategory Category { get; }

    /// <summary>
    /// Why this migration is the category it declares, in one or two sentences a reviewer can
    /// check against the generated SQL. Never empty.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// For a <see cref="MigrationCategory.Contract"/>: the EF migration id of the Expand this
    /// migration contracts. Unset on any other category.
    /// </summary>
    public string? Contracts { get; init; }
}
