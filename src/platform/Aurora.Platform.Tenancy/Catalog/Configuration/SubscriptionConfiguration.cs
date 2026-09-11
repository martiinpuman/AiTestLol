using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aurora.Platform.Tenancy.Catalog.Configuration;

/// <summary>
/// <c>catalog.subscription</c> (ADR-0007 §9.2).
/// </summary>
/// <remarks>
/// The non-overlap rule — one tenant, one subscription on any given day — is an exclusion
/// constraint, <c>ex_subscription_no_overlap</c>, which EF Core cannot express in the model; it is
/// added by SQL in the <c>InitialCatalog</c> migration and proven by an integration test.
/// </remarks>
internal sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("subscription", table =>
        {
            table.HasCheckConstraint("ck_subscription_seats_positive", "seats > 0");
            table.HasCheckConstraint(
                "ck_subscription_ends_after_it_starts",
                "valid_to IS NULL OR valid_to > valid_from");
        });

        builder.HasKey(subscription => subscription.Id).HasName("pk_subscription");

        builder.Property(subscription => subscription.Id).HasColumnName("id");
        builder.Property(subscription => subscription.TenantId).HasColumnName("tenant_id");
        builder.Property(subscription => subscription.Plan).HasColumnName("plan").HasMaxLength(Subscription.MaxPlanLength);
        builder.Property(subscription => subscription.Seats).HasColumnName("seats");
        builder.Property(subscription => subscription.ValidFrom).HasColumnName("valid_from");
        builder.Property(subscription => subscription.ValidTo).HasColumnName("valid_to");

        builder.HasIndex(subscription => subscription.TenantId).HasDatabaseName("ix_subscription_tenant_id");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(subscription => subscription.TenantId)
            .HasConstraintName("fk_subscription_tenant")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
