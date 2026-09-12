using System;
using Aurora.Platform.Tenancy.Contracts;
using Aurora.SharedKernel;

namespace Aurora.Platform.Tenancy.Routing;

/// <summary>
/// The tenant exists but is in a state the application path may not connect to
/// (<see cref="TenantConnectionResolver.IsRoutable"/>).
/// </summary>
internal sealed class TenantNotRoutableException : Exception
{
    public TenantNotRoutableException(TenantId tenantId, TenantState state)
        : base($"Tenant {tenantId} is {state}; the application path connects only to an Active or Suspended tenant (ADR-0007 §11.4).")
    {
        TenantId = tenantId;
        State = state;
    }

    public TenantId TenantId { get; }

    public TenantState State { get; }
}
