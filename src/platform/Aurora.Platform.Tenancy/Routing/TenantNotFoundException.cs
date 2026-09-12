using System;
using Aurora.SharedKernel;

namespace Aurora.Platform.Tenancy.Routing;

/// <summary>No <c>catalog.tenant</c> row has the id that was resolved.</summary>
internal sealed class TenantNotFoundException : Exception
{
    public TenantNotFoundException(TenantId tenantId)
        : base($"No catalog.tenant row has id {tenantId}.")
    {
        TenantId = tenantId;
    }

    public TenantId TenantId { get; }
}
