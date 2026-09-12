using System;
using System.Collections.Generic;
using Npgsql;

namespace Aurora.Platform.Tenancy.IntegrationTests;

/// <summary>
/// One well-formed row for an append-only table, with the statement that inserts it. The shape
/// each writer will produce, minus the writer: B-19 builds no writer, so the tests write the rows
/// themselves (<c>solution-layout.md</c> §6.4 item 5, scope boundary). Shared by the tests that
/// write as <c>aurora_app</c> (<c>CatalogAppendOnlyTests</c>) and the guard probe that seeds as the
/// owner (<c>CatalogAppendOnlyGuardTests</c>), so both exercise the same column set.
/// </summary>
internal sealed record AppendOnlyRow(string Table, Guid Id, string Sql, IReadOnlyList<(string Name, object Value)> Parameters)
{
    public static IEnumerable<AppendOnlyRow> OneForEachTable(Guid tenantId)
    {
        Guid operatorEventId = Guid.CreateVersion7();
        yield return new AppendOnlyRow(
            "operator_audit_event",
            operatorEventId,
            "INSERT INTO catalog.operator_audit_event (id, occurred_at, tenant_id, actor_type, actor_id, action, reason_code, correlation_id, detail) "
            + "VALUES (@id, @at, @tenant, 'Operator', 'operator:probe', 'tenant.support_access.granted', 'support:ticket', NULL, '{\"valid_for_minutes\": 30}'::jsonb)",
            [("id", operatorEventId), ("at", Unique.Now), ("tenant", tenantId)]);

        Guid erasureId = Guid.CreateVersion7();
        yield return new AppendOnlyRow(
            "erasure_replay_log",
            erasureId,
            "INSERT INTO catalog.erasure_replay_log (id, tenant_id, subject_ref, requested_by, executed_at, affected, correlation_id) "
            + "VALUES (@id, @tenant, 'subject:7f3a', 'user:probe', @at, '[\"party.person.display_name\"]'::jsonb, NULL)",
            [("id", erasureId), ("tenant", tenantId), ("at", Unique.Now)]);
    }

    public NpgsqlCommand Insert(NpgsqlConnection connection, NpgsqlTransaction? transaction = null)
    {
        var command = new NpgsqlCommand(Sql, connection, transaction);
        foreach ((string name, object value) in Parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return command;
    }
}
