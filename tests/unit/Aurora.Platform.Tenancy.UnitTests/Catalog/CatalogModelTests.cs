using System.Collections.Generic;
using System.Linq;
using Aurora.Platform.Tenancy.Catalog;
using Aurora.Platform.Tenancy.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Shouldly;
using Xunit;

namespace Aurora.Platform.Tenancy.UnitTests.Catalog;

/// <summary>
/// Properties of the catalog model that need the provider's type mappings but no database, so
/// they run in verify.sh stage 6 with Docker stopped.
/// </summary>
public sealed class CatalogModelTests
{
    [Fact]
    public void Every_entity_maps_to_a_table_in_the_catalog_schema_and_nowhere_else()
    {
        using CatalogDbContext context = OfflineCatalog.Open();

        IEnumerable<IEntityType> entities = context.Model.GetEntityTypes();

        entities.ShouldNotBeEmpty();
        foreach (IEntityType entity in entities)
        {
            entity.GetSchema().ShouldBe(CatalogDbContext.Schema, entity.DisplayName());
            entity.GetTableName().ShouldNotBeNull(entity.DisplayName());
        }
    }

    [Fact]
    public void The_model_maps_exactly_the_tables_ADR_0007_9_2_names_for_this_task()
    {
        using CatalogDbContext context = OfflineCatalog.Open();

        IEnumerable<string> tables = context.Model.GetEntityTypes().Select(entity => entity.GetTableName()!);

        tables.OrderBy(name => name, System.StringComparer.Ordinal)
            .ShouldBe(CatalogSchemaAllowlist.Columns.Keys.OrderBy(name => name, System.StringComparer.Ordinal));
    }

    [Fact]
    public void The_model_snapshot_is_in_step_with_the_model_so_no_change_ships_without_a_migration()
    {
        using CatalogDbContext context = OfflineCatalog.Open();

        // EF compares the last migration's snapshot with the model built from the code. A property
        // added without `dotnet ef migrations add` fails here, in the unit stage, not in production.
        context.Database.HasPendingModelChanges().ShouldBeFalse(
            "the catalog model differs from Migrations/CatalogDbContextModelSnapshot.cs; scaffold a migration");
    }

    [Fact]
    public void Queries_do_not_track_by_default()
    {
        using CatalogDbContext context = OfflineCatalog.Open();

        // ADR-0003 rule 4, fitness rule Q2.
        context.ChangeTracker.QueryTrackingBehavior.ShouldBe(QueryTrackingBehavior.NoTrackingWithIdentityResolution);
    }

    [Fact]
    public void Every_typed_identifier_and_value_object_lands_in_a_plain_column()
    {
        using CatalogDbContext context = OfflineCatalog.Open();

        IEntityType tenant = context.Model.FindEntityType(typeof(Tenant))!;

        tenant.FindProperty(nameof(Tenant.Id))!.GetColumnType().ShouldBe("uuid");
        tenant.FindProperty(nameof(Tenant.Key))!.GetColumnType().ShouldBe("character varying(40)");
        tenant.FindProperty(nameof(Tenant.ClusterId))!.GetColumnType().ShouldBe("character varying(40)");
        tenant.FindProperty(nameof(Tenant.ResidencyRegion))!.GetColumnType().ShouldBe("character varying(32)");
        tenant.FindProperty(nameof(Tenant.State))!.GetColumnType().ShouldBe("character varying(32)");
        tenant.FindProperty(nameof(Tenant.CreatedAt))!.GetColumnType().ShouldBe("timestamp with time zone");
    }
}
