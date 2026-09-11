using Aurora.Platform.Tenancy.Catalog.Configuration;
using Aurora.Platform.Tenancy.Contracts;
using Aurora.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Aurora.Platform.Tenancy.Catalog;

/// <summary>
/// The shared catalog database: tenant registry, routing, clusters, subscriptions and installed
/// packages, in schema <c>catalog</c> (ADR-0007 §9).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the one conventionally registered context</b> (ADR-0003 rule 3, ADR-0007 §4.2). It
/// has a public constructor and is added with <c>AddDbContext</c> by
/// <see cref="CatalogServiceCollectionExtensions.AddCatalogDatabase"/>, because it needs no tenant
/// to be resolved: it is where tenants are resolved <em>from</em>. Every tenant context is the
/// opposite — internal constructor, never registered, obtained only through
/// <c>ITenantDbContextFactory&lt;T&gt;</c> with a <c>TenantScope</c> in hand (B-06).
/// </para>
/// <para>
/// <b>It holds no tenant business data</b> (ADR-0007 §9.3): a test over the model, and one over
/// the migrated schema, fail when a column that looks like business data is added.
/// </para>
/// <para>
/// Queries do not track by default (ADR-0003 rule 4, fitness rule Q2); a unit of work that writes
/// opts in with <c>AsTracking()</c> or by attaching what it loads.
/// </para>
/// </remarks>
internal sealed class CatalogDbContext : DbContext
{
    public const string Schema = "catalog";
    public const string MigrationsHistoryTable = "__EFMigrationsHistory";

    private const int StateNameMaxLength = 32;

    public CatalogDbContext(DbContextOptions<CatalogDbContext> options)
        : base(options)
    {
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<TenantHost> TenantHosts => Set<TenantHost>();

    public DbSet<DatabaseCluster> DatabaseClusters => Set<DatabaseCluster>();

    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    public DbSet<InstalledPackage> InstalledPackages => Set<InstalledPackage>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTrackingWithIdentityResolution);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<TenantId>().HaveConversion<EntityIdConverter<TenantId>>();
        configurationBuilder.Properties<SubscriptionId>().HaveConversion<EntityIdConverter<SubscriptionId>>();

        configurationBuilder.Properties<TenantKey>()
            .HaveConversion<TenantKeyConverter>()
            .HaveMaxLength(TenantKey.MaxLength);
        configurationBuilder.Properties<ClusterId>()
            .HaveConversion<ClusterIdConverter>()
            .HaveMaxLength(ClusterId.MaxLength);
        configurationBuilder.Properties<Region>()
            .HaveConversion<RegionConverter>()
            .HaveMaxLength(Region.MaxLength);
        configurationBuilder.Properties<SecretReference>()
            .HaveConversion<SecretReferenceConverter>()
            .HaveMaxLength(SecretReference.MaxLength);

        // States are stored by name so a row is readable in psql and reordering an enum can never
        // relabel a tenant. Each table's check constraint lists the names (see the configurations).
        configurationBuilder.Properties<TenantState>().HaveConversion<string>().HaveMaxLength(StateNameMaxLength);
        configurationBuilder.Properties<DatabaseClusterState>().HaveConversion<string>().HaveMaxLength(StateNameMaxLength);
        configurationBuilder.Properties<InstalledPackageState>().HaveConversion<string>().HaveMaxLength(StateNameMaxLength);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        // btree_gist lets an exclusion constraint compare a uuid with '=' next to a daterange
        // with '&&' - what keeps one tenant from holding two subscriptions on the same day.
        modelBuilder.HasPostgresExtension("btree_gist");

        // Applied one by one rather than by assembly scan, so that the set of tables in the catalog
        // is this list and a reviewer can see it change.
        modelBuilder.ApplyConfiguration(new DatabaseClusterConfiguration());
        modelBuilder.ApplyConfiguration(new TenantConfiguration());
        modelBuilder.ApplyConfiguration(new TenantHostConfiguration());
        modelBuilder.ApplyConfiguration(new SubscriptionConfiguration());
        modelBuilder.ApplyConfiguration(new InstalledPackageConfiguration());
    }
}
