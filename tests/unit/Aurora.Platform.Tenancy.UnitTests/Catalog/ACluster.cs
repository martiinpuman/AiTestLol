using Aurora.Platform.Tenancy.Catalog;
using Aurora.Platform.Tenancy.Contracts;

namespace Aurora.Platform.Tenancy.UnitTests.Catalog;

/// <summary>A cluster builder with sane defaults (testing-strategy.md §3).</summary>
internal sealed class ACluster
{
    private ClusterId _id = ClusterId.Parse("nz-1", null);
    private Region _region = Region.Parse("nz", null);
    private string _host = "pg-nz-1.internal";
    private int _port = 5432;
    private string _maintenanceDatabase = "postgres";
    private SecretReference _adminSecretRef = SecretReference.Of("vault://kv/aurora/clusters/nz-1/admin");
    private readonly SecretReference _migratorSecretRef = SecretReference.Of("vault://kv/aurora/clusters/nz-1/migrator");
    private readonly SecretReference _appSecretRef = SecretReference.Of("vault://kv/aurora/clusters/nz-1/app");
    private int _maxTenants = 1_000;

    public ACluster WithId(string id)
    {
        _id = ClusterId.Parse(id, null);
        return this;
    }

    public ACluster InRegion(string region)
    {
        _region = Region.Parse(region, null);
        return this;
    }

    public ACluster WithHost(string host)
    {
        _host = host;
        return this;
    }

    public ACluster WithPort(int port)
    {
        _port = port;
        return this;
    }

    public ACluster WithMaintenanceDatabase(string maintenanceDatabase)
    {
        _maintenanceDatabase = maintenanceDatabase;
        return this;
    }

    public ACluster WithAdminSecretRef(SecretReference reference)
    {
        _adminSecretRef = reference;
        return this;
    }

    public ACluster WithMaxTenants(int maxTenants)
    {
        _maxTenants = maxTenants;
        return this;
    }

    public DatabaseCluster Build() =>
        DatabaseCluster.Register(
            _id,
            _region,
            _host,
            _port,
            _maintenanceDatabase,
            _adminSecretRef,
            _migratorSecretRef,
            _appSecretRef,
            _maxTenants);
}
