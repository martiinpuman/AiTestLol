using System;
using Aurora.Platform.Tenancy.Catalog;
using Aurora.Platform.Tenancy.Contracts;
using Aurora.SharedKernel;

namespace Aurora.Platform.Tenancy.IntegrationTests;

/// <summary>
/// Values that cannot collide across tests sharing one catalog database, so no test needs to
/// clean up after another.
/// </summary>
internal static class Unique
{
    public static readonly DateTimeOffset Now = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

    public static TenantKey TenantKey() => Contracts.TenantKey.Parse("t-" + Suffix(), null);

    public static ClusterId ClusterId() => Contracts.ClusterId.Parse("c-" + Suffix(), null);

    public static string Host() => "h-" + Suffix() + ".aurora.test";

    /// <summary>A PostgreSQL identifier no other test will mint, for a table or database a test creates and drops.</summary>
    public static string Identifier(string prefix) => prefix + "_" + Suffix();

    public static DatabaseCluster Cluster(string region = "nz") =>
        DatabaseCluster.Register(
            ClusterId(),
            Region.Parse(region, null),
            "pg-" + Suffix() + ".internal",
            5432,
            "postgres",
            SecretReference.Of("vault://kv/aurora/test/admin"),
            SecretReference.Of("vault://kv/aurora/test/migrator"),
            SecretReference.Of("vault://kv/aurora/test/app"),
            1_000);

    public static Tenant Tenant(DatabaseCluster cluster) =>
        Catalog.Tenant.Reserve(TenantId.Create(), TenantKey(), "Acme Trading Ltd", cluster, "standard", Now);

    private static string Suffix() => Guid.NewGuid().ToString("N")[..12];
}
