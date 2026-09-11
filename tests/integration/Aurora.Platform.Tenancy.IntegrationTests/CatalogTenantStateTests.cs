using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aurora.Platform.Tenancy.Catalog;
using Aurora.Platform.Tenancy.Contracts;
using Aurora.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Xunit;

namespace Aurora.Platform.Tenancy.IntegrationTests;

/// <summary>
/// The whole of <c>catalog.tenant.state</c> — every state in ADR-0007 §9.2's set stores and reads
/// back, and nothing outside it can be stored at all.
/// </summary>
/// <remarks>
/// <para>
/// B-05 declares the full lifecycle rather than the states it uses itself, because three later
/// tasks stamp states this migration has to already admit: <c>SchemaBlocked</c> when the
/// connected-database identity check catches a mis-route (§4.3) and when a migration quarantines
/// a tenant (§7.4), and <c>ProvisioningFailed</c> — the only state from which the operator console
/// may destroy a database (§8) — when provisioning exhausts its retry budget. Widening a check
/// constraint under live tenants is the kind of change the expand/contract rule exists to avoid,
/// so it is done once, here.
/// </para>
/// <para>
/// Asserting that the constraint's text names the eight states (<c>CatalogSchemaTests</c>) is not
/// the same as asserting a row can hold them: the value converter, the column width and the two
/// state-dependent check constraints all sit between the enum and the stored row. This test writes
/// each state and reads it back through the model.
/// </para>
/// </remarks>
[Collection(CatalogDatabaseSuite.Name)]
[Trait("Category", "Integration")]
public sealed class CatalogTenantStateTests
{
    private const string CheckViolation = "23514";

    private readonly CatalogDatabaseFixture _catalog;

    public CatalogTenantStateTests(CatalogDatabaseFixture catalog) => _catalog = catalog;

    [Fact]
    public async Task Every_state_ADR_0007_9_2_names_stores_and_reads_back()
    {
        TenantState[] lifecycle = Enum.GetValues<TenantState>();

        // The floor the later tasks depend on, stated as a number so this cannot quietly check a
        // shorter list than ADR-0007 §9.2 names.
        lifecycle.Length.ShouldBe(8);
        lifecycle.ShouldContain(TenantState.SchemaBlocked);
        lifecycle.ShouldContain(TenantState.ProvisioningFailed);

        DatabaseCluster cluster = Unique.Cluster();
        await using (CatalogDbContext writer = _catalog.OpenAsApp())
        {
            writer.DatabaseClusters.Add(cluster);
            await writer.SaveChangesAsync();
        }

        List<TenantState> readBack = [];
        foreach (TenantState state in lifecycle)
        {
            Tenant tenant = Unique.Tenant(cluster);
            await using (CatalogDbContext writer = _catalog.OpenAsApp())
            {
                writer.Tenants.Add(tenant);
                await writer.SaveChangesAsync();
            }

            // B-05 gives Tenant only the two transitions the provisioning saga's ends need
            // (ADR-0007 §8 steps 1 and 8); the methods that reach the other six arrive with the
            // tasks that drive them. Until then the state is moved in SQL, which is also the
            // stronger test: it proves the *database* accepts the value, not that C# does.
            await StampAsync(tenant.Id, state);

            await using CatalogDbContext reader = _catalog.OpenAsApp();
            readBack.Add((await reader.Tenants.SingleAsync(candidate => candidate.Id == tenant.Id)).State);
        }

        readBack.ShouldBe(lifecycle);
    }

    [Theory]
    [InlineData("Provisioned")]
    [InlineData("active")]
    [InlineData("Blocked")]
    [InlineData("")]
    public async Task A_state_the_enum_does_not_name_is_refused_however_plausible_it_looks(string state)
    {
        DatabaseCluster cluster = Unique.Cluster();
        Tenant tenant = Unique.Tenant(cluster);
        await using (CatalogDbContext writer = _catalog.OpenAsApp())
        {
            writer.DatabaseClusters.Add(cluster);
            writer.Tenants.Add(tenant);
            await writer.SaveChangesAsync();
        }

        PostgresException refused = await Should.ThrowAsync<PostgresException>(() => StampAsync(tenant.Id, state));

        refused.SqlState.ShouldBe(CheckViolation);
        refused.ConstraintName.ShouldBe("ck_tenant_state");
    }

    private Task StampAsync(TenantId tenant, TenantState state) => StampAsync(tenant, state.ToString());

    private async Task StampAsync(TenantId tenant, string state)
    {
        await using NpgsqlConnection connection = await _catalog.OpenAppConnectionAsync();

        // deleted_at is required exactly when the state is Deleted (ck_tenant_deleted_at_matches_state),
        // so it moves with the state rather than being a second statement that could be forgotten.
        await using var command = new NpgsqlCommand(
            "UPDATE catalog.tenant SET state = @state, "
            + "deleted_at = CASE WHEN @state = 'Deleted' THEN @now ELSE NULL END "
            + "WHERE id = @id",
            connection);
        command.Parameters.AddWithValue("state", state);
        command.Parameters.AddWithValue("now", Unique.Now);
        command.Parameters.AddWithValue("id", tenant.Value);

        (await command.ExecuteNonQueryAsync()).ShouldBe(1);
    }
}
