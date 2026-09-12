namespace Aurora.Platform.Tenancy;

/// <summary>
/// Which of ADR-0007 §5.2's two pool-setting columns a host runs under. The web host serves
/// interactive work that is bounded and shared; the worker runs batch work that is not.
/// </summary>
/// <remarks>
/// Public because it is the one thing a host has to tell
/// <c>TenancyServiceCollectionExtensions.AddTenantConnectionResolver</c>; the settings a
/// profile stands for stay internal, in <c>TenantPoolSettings</c>, so that no caller outside this
/// assembly learns what a connection string looks like (ADR-0007 §3.5).
/// </remarks>
public enum TenantPoolProfile
{
    /// <summary><c>Aurora.Web</c>: maximum pool size 10, command timeout 30 s.</summary>
    Web,

    /// <summary><c>Aurora.Worker</c>: maximum pool size 5, command timeout 300 s.</summary>
    Worker,
}
