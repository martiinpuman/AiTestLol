namespace Aurora.Platform.Tenancy.Catalog;

/// <summary>
/// The login roles every cluster has (ADR-0004 rule 2). The names are the same on every cluster,
/// which is why <c>catalog.database_cluster</c> stores a secret reference per role and no role
/// name: the resolver composes <c>Username=aurora_app</c> itself.
/// </summary>
internal static class ClusterRoles
{
    /// <summary>
    /// The request path's role: <c>SELECT</c>/<c>INSERT</c>/<c>UPDATE</c>/<c>DELETE</c>, no DDL.
    /// One per cluster under ADR-0007 §3.5 stage 1; one per tenant database under stage 2
    /// (<c>FOLLOWUP-001</c>), which changes the resolver and nothing that calls it.
    /// </summary>
    public const string App = "aurora_app";
}
