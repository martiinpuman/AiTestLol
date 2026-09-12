using Aurora.Platform.Tenancy.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aurora.Platform.Tenancy.Catalog.Configuration;

/// <summary><c>catalog.installed_package</c> (ADR-0007 §9.2, ADR-0008 §4).</summary>
internal sealed class InstalledPackageConfiguration : IEntityTypeConfiguration<InstalledPackage>
{
    public void Configure(EntityTypeBuilder<InstalledPackage> builder)
    {
        builder.ToTable("installed_package", table =>
        {
            table.HasCheckConstraint(
                "ck_installed_package_state",
                StateNames.CheckConstraintSql<InstalledPackageState>("state"));
            table.HasCheckConstraint(
                "ck_installed_package_id_well_formed",
                PackageIdFormat.CheckConstraintSql("package_id"));
        });

        builder.HasKey(package => new { package.TenantId, package.PackageId }).HasName("pk_installed_package");

        builder.Property(package => package.TenantId).HasColumnName("tenant_id");
        builder.Property(package => package.PackageId).HasColumnName("package_id").HasMaxLength(PackageIdFormat.MaxLength);
        builder.Property(package => package.Version).HasColumnName("version").HasMaxLength(InstalledPackage.MaxVersionLength);
        builder.Property(package => package.State).HasColumnName("state");
        builder.Property(package => package.InstalledAt).HasColumnName("installed_at");
        builder.Property(package => package.InstalledBy).HasColumnName("installed_by").HasMaxLength(InstalledPackage.MaxInstalledByLength);

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(package => package.TenantId)
            .HasConstraintName("fk_installed_package_tenant")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
