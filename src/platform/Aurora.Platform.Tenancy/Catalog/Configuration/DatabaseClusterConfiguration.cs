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
    }
}
