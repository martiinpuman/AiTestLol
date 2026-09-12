using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aurora.Platform.Tenancy.Catalog.Configuration;

/// <summary><c>catalog.database_cluster</c> (ADR-0007 §9.2).</summary>
internal sealed class DatabaseClusterConfiguration : IEntityTypeConfiguration<DatabaseCluster>
{
    /// <summary>
    /// The characters no secret <em>reference</em> contains and every connection string or
    /// <c>key=value</c> credential does. Mirrors <see cref="SecretReference.IsWellFormed"/>.
    /// </summary>
    private const string CredentialShapedPattern = "[=;[:space:]]";

    public void Configure(EntityTypeBuilder<DatabaseCluster> builder)
    {
        builder.ToTable("database_cluster", table =>
        {
            table.HasCheckConstraint(
                "ck_database_cluster_state",
                StateNames.CheckConstraintSql<DatabaseClusterState>("state"));
            table.HasCheckConstraint(
                "ck_database_cluster_id_well_formed",
                "id ~ '^[a-z][a-z0-9]*(-[a-z0-9]+)*$'");
            table.HasCheckConstraint(
                "ck_database_cluster_port_is_tcp_port",
                $"port BETWEEN {DatabaseCluster.MinPort} AND {DatabaseCluster.MaxPort}");
            table.HasCheckConstraint(
                "ck_database_cluster_max_tenants_positive",
                "max_tenants > 0");
            table.HasCheckConstraint(
                "ck_database_cluster_secret_refs_are_references",
                $"admin_secret_ref !~ '{CredentialShapedPattern}' " +
                $"AND migrator_secret_ref !~ '{CredentialShapedPattern}' " +
                $"AND app_secret_ref !~ '{CredentialShapedPattern}'");

            // A host name is case-insensitive, so two spellings of one host would be two rows on one
            // endpoint that ux_database_cluster_host_port below could not tell apart (ADR-0034 3.2;
            // PR #18 M-1/M-2). The same rule tenant_host keeps, for the same reason.
            table.HasCheckConstraint("ck_database_cluster_host_lower_case", "host = lower(host)");
        });

        builder.HasKey(cluster => cluster.Id).HasName("pk_database_cluster");

        // The target of catalog.tenant's composite foreign key: a tenant names its cluster *and*
        // its region, and the pair must exist here, so a tenant cannot be routed out of its region
        // (ADR-0007 11.3).
        builder.HasAlternateKey(cluster => new { cluster.Id, cluster.Region }).HasName("ak_database_cluster_id_region");

        builder.Property(cluster => cluster.Id).HasColumnName("id");
        builder.Property(cluster => cluster.Region).HasColumnName("region");
        builder.Property(cluster => cluster.Host).HasColumnName("host").HasMaxLength(DatabaseCluster.MaxHostLength);
        builder.Property(cluster => cluster.Port).HasColumnName("port");
        builder.Property(cluster => cluster.MaintenanceDatabase)
            .HasColumnName("maintenance_database")
            .HasMaxLength(PostgresIdentifier.MaxLength);
        builder.Property(cluster => cluster.AdminSecretRef).HasColumnName("admin_secret_ref");
        builder.Property(cluster => cluster.MigratorSecretRef).HasColumnName("migrator_secret_ref");
        builder.Property(cluster => cluster.AppSecretRef).HasColumnName("app_secret_ref");
        builder.Property(cluster => cluster.MaxTenants).HasColumnName("max_tenants");
        builder.Property(cluster => cluster.State).HasColumnName("state");

        // Placement (ADR-0007 8 step 1) asks for accepting clusters in one region.
        builder.HasIndex(cluster => new { cluster.Region, cluster.State }).HasDatabaseName("ix_database_cluster_region_state");

        // The routing-uniqueness axis is the physical endpoint (ADR-0034 3.1): with this index
        // cluster_id -> (host, port) is injective, fk_tenant_cluster_in_region makes it total for
        // every non-deleted tenant, and ux_tenant_cluster_id_database_name makes
        // (cluster_id, database_name) unique - so (host, port, database_name), the triple the
        // resolver composes into a connection string, is unique across catalog.tenant. It closes
        // the third executed variant of the tenant-takeover finding: two cluster rows on one
        // server, one tenant each, the attacker's database_name copied from the victim's. It does
        // not cover two names for one server (a CNAME, a second DNS record, a failover alias, an
        // IP literal beside a host name); that is TenantIdentityStamp's job (ADR-0034 4). A read
        // replica or a pooler endpoint is not a row here (3.2); if either ever becomes one, this
        // index is wrong as written and ADR-0034 9 is where to start.
        builder.HasIndex(cluster => new { cluster.Host, cluster.Port })
            .IsUnique()
            .HasDatabaseName("ux_database_cluster_host_port");
    }
}
