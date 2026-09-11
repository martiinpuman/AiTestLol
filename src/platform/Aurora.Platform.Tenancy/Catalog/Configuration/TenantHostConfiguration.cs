using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aurora.Platform.Tenancy.Catalog.Configuration;

/// <summary><c>catalog.tenant_host</c> (ADR-0007 §9.2, §3.2 strategy 1).</summary>
internal sealed class TenantHostConfiguration : IEntityTypeConfiguration<TenantHost>
{
    public void Configure(EntityTypeBuilder<TenantHost> builder)
    {
        builder.ToTable("tenant_host", table =>
            table.HasCheckConstraint("ck_tenant_host_lower_case", "host = lower(host)"));

        builder.HasKey(host => host.Host).HasName("pk_tenant_host");

        builder.Property(host => host.Host).HasColumnName("host").HasMaxLength(TenantHost.MaxHostLength);
        builder.Property(host => host.TenantId).HasColumnName("tenant_id");
        builder.Property(host => host.IsPrimary).HasColumnName("is_primary");
        builder.Property(host => host.VerifiedAt).HasColumnName("verified_at");

        builder.HasIndex(host => host.TenantId, "ix_tenant_host_tenant_id");

        // One primary host per tenant, as a fact the database keeps rather than a rule code follows.
        builder.HasIndex(host => host.TenantId, "ux_tenant_host_primary")
            .IsUnique()
            .HasFilter("is_primary");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(host => host.TenantId)
            .HasConstraintName("fk_tenant_host_tenant")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
