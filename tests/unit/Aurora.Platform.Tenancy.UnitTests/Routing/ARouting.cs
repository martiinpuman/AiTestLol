using System;
using Aurora.Platform.Tenancy.Contracts;
using Aurora.Platform.Tenancy.Routing;

namespace Aurora.Platform.Tenancy.UnitTests.Routing;

/// <summary>A routing row builder with sane defaults (testing-strategy.md §3): an active tenant on a real-looking cluster.</summary>
internal sealed class ARouting
{
    public const string DefaultHost = "pg-nz-1.internal";
    public const int DefaultPort = 5432;
    public const string DefaultAppSecretRef = "env:AURORA_NZ1_APP_PASSWORD";

    private Guid _tenantId = Guid.CreateVersion7();
    private string _tenantKey = "acme-trading";
    private TenantState _state = TenantState.Active;
    private string _clusterId = "nz-1";
    private string _host = DefaultHost;
    private int _port = DefaultPort;
    private string _appSecretRef = DefaultAppSecretRef;
    private string? _databaseName = "aurora_t_acme_trading";
    private string? _region = "nz";
    private bool _tombstoned;

    public ARouting WithTenantId(Guid tenantId)
    {
        _tenantId = tenantId;
        return this;
    }

    public ARouting WithKey(string tenantKey)
    {
        _tenantKey = tenantKey;
        _databaseName = "aurora_t_" + tenantKey.Replace('-', '_');
        return this;
    }

    /// <summary>A database name that is not the one the key derives; what a restore re-points a tenant at (ADR-0007 §11.2).</summary>
    public ARouting WithDatabaseName(string databaseName)
    {
        _databaseName = databaseName;
        return this;
    }

    public ARouting InState(TenantState state)
    {
        _state = state;
        return this;
    }

    public ARouting OnCluster(string clusterId, string host, int port)
    {
        _clusterId = clusterId;
        _host = host;
        _port = port;
        return this;
    }

    public ARouting WithAppSecretRef(string reference)
    {
        _appSecretRef = reference;
        return this;
    }

    public ARouting InRegion(string region)
    {
        _region = region;
        return this;
    }

    /// <summary>ADR-0007 §11.4: a deleted tenant keeps id, key and dates; every routing column is blank.</summary>
    public ARouting Tombstoned()
    {
        _state = TenantState.Deleted;
        _tombstoned = true;
        return this;
    }

    public ClusterEndpoint BuildCluster() =>
        new(_clusterId, _host, _port, "postgres", "env:AURORA_NZ1_ADMIN_PASSWORD", "env:AURORA_NZ1_MIGRATOR_PASSWORD", _appSecretRef);

    public TenantRouting Build() => _tombstoned
        ? new TenantRouting(_tenantId, _tenantKey, _state, Cluster: null, DatabaseName: null, ResidencyRegion: null)
        : new TenantRouting(_tenantId, _tenantKey, _state, BuildCluster(), _databaseName, _region);
}
