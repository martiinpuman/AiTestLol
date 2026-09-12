namespace Aurora.Platform.Tenancy.Contracts;

/// <summary>
/// The two core schema versions this build of the code ships with (ADR-0007 §7.5, placed here by
/// ADR-0027 §4): the version its migrations produce, and the oldest it still runs correctly against.
/// </summary>
/// <remarks>
/// <para>
/// <b>Who reads them.</b> The scope factory (B-06.3) compares a tenant's
/// <see cref="TenantScope.SchemaVersion"/> with both on every scope open and fails the open when
/// the tenant is below <see cref="MinimumSupported"/> or above <see cref="Current"/> - the §7.5
/// table, and the only place it is applied. The migration runner reads <see cref="Current"/> as its
/// target. Neither is consulted on the DDL path, which holds a <c>TenantDatabaseHandle</c> and has
/// no gate to bypass (ADR-0027 §4).
/// </para>
/// <para>
/// <b>Who keeps them honest.</b> A hand-maintained constant that nothing checks is a comment, so
/// ADR-0027 §4 names two tests: <see cref="Current"/> equals the highest ordinal across the
/// registered <c>IModuleSchemaMigrator</c>s, and <c>Current - MinimumSupported &lt;= 1</c> (§7.5
/// defines the minimum as N−1). Both are B-08.3's, because no migrator is registered yet for the
/// first to read. Until then <c>CoreSchemaVersionTests</c> pins the values below and holds
/// <see cref="MinimumSupported"/> to never exceed <see cref="Current"/>.
/// </para>
/// <para>
/// <b>Why both are zero today.</b> No row has landed a core migration: the tenant database's
/// <c>platform</c> schema arrives with B-07.2, and the first module schema after that. The highest
/// ordinal across zero migrators is the initial version, and a minimum below zero does not exist,
/// so the two coincide. The row that lands the first migration raises <see cref="Current"/> in the
/// same commit.
/// </para>
/// </remarks>
public static class CoreSchemaVersion
{
    /// <summary>The version this build's migrations produce.</summary>
    public static SchemaVersion Current { get; } = SchemaVersion.Of(0);

    /// <summary>
    /// The oldest version this build runs correctly against - one release behind
    /// <see cref="Current"/>, because expand migrations deploy ahead of the code that uses them.
    /// </summary>
    public static SchemaVersion MinimumSupported { get; } = SchemaVersion.Of(0);
}
