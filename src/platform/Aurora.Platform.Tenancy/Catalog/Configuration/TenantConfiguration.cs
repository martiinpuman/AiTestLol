using Aurora.Platform.Tenancy.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aurora.Platform.Tenancy.Catalog.Configuration;

/// <summary><c>catalog.tenant</c> (ADR-0007 §9.2).</summary>
internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenant", table =>
        {
            table.HasCheckConstraint(
                "ck_tenant_state",
                StateNames.CheckConstraintSql<TenantState>("state"));

            // The same rule TenantKey enforces, so no row can hold a key the type refuses to read.
            table.HasCheckConstraint(
                "ck_tenant_key_well_formed",
                $"key ~ '^[a-z][a-z0-9]*(-[a-z0-9]+)*$' AND length(key) >= {TenantKey.MinLength}");

            // ADR-0007 11.4: a deleted tenant is tombstoned to id, key and dates. Every other state
            // is a tenant that must be routable, so the routing columns are required there.
            table.HasCheckConstraint(
                "ck_tenant_routing_present_unless_deleted",
                $"state = '{nameof(TenantState.Deleted)}' OR (display_name IS NOT NULL AND cluster_id IS NOT NULL " +
                "AND database_name IS NOT NULL AND residency_region IS NOT NULL AND plan IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_tenant_deleted_at_matches_state",
                $"(state = '{nameof(TenantState.Deleted)}') = (deleted_at IS NOT NULL)");
        });

        builder.HasKey(tenant => tenant.Id).HasName("pk_tenant");

        builder.Property(tenant => tenant.Id).HasColumnName("id");
        builder.Property(tenant => tenant.Key).HasColumnName("key");
        builder.Property(tenant => tenant.DisplayName).HasColumnName("display_name").HasMaxLength(Tenant.MaxDisplayNameLength);
        builder.Property(tenant => tenant.State).HasColumnName("state");
        builder.Property(tenant => tenant.ClusterId).HasColumnName("cluster_id");
        builder.Property(tenant => tenant.DatabaseName).HasColumnName("database_name").HasMaxLength(PostgresIdentifier.MaxLength);
        builder.Property(tenant => tenant.ResidencyRegion).HasColumnName("residency_region");
        builder.Property(tenant => tenant.CoreSchemaVersion).HasColumnName("core_schema_version");
        builder.Property(tenant => tenant.Plan).HasColumnName("plan").HasMaxLength(Tenant.MaxPlanLength);
        builder.Property(tenant => tenant.CreatedAt).HasColumnName("created_at");
        builder.Property(tenant => tenant.ActivatedAt).HasColumnName("activated_at");
        builder.Property(tenant => tenant.SuspendedAt).HasColumnName("suspended_at");
        builder.Property(tenant => tenant.DeletionDueAt).HasColumnName("deletion_due_at");
        builder.Property(tenant => tenant.DeletedAt).HasColumnName("deleted_at");
        builder.Property(tenant => tenant.LastActivityAt).HasColumnName("last_activity_at");

        // Unique for the life of the row, tombstone included: a key is never reused (ADR-0007 11.4).
        builder.HasIndex(tenant => tenant.Key).IsUnique().HasDatabaseName("ux_tenant_key");

        // The outbox sweep tiers active tenants by recent activity (ADR-0007 10.3) and every fan-out
        // job selects on state (10.2).
        builder.HasIndex(tenant => new { tenant.State, tenant.LastActivityAt }).HasDatabaseName("ix_tenant_state_last_activity_at");

        // Routing never crosses regions (ADR-0007 11.3): the cluster *and* the region must be a
        // pair that exists. Both columns are null only on a tombstone, where the check above holds
        // instead. Restrict, because a cluster with tenants on it is not something to delete.
        builder.HasIndex(tenant => new { tenant.ClusterId, tenant.ResidencyRegion }).HasDatabaseName("ix_tenant_cluster_id_residency_region");
        builder.HasOne<DatabaseCluster>()
            .WithMany()
            .HasForeignKey(tenant => new { tenant.ClusterId, tenant.ResidencyRegion })
            .HasPrincipalKey(cluster => new { cluster.Id, cluster.Region })
            .HasConstraintName("fk_tenant_cluster_in_region")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
