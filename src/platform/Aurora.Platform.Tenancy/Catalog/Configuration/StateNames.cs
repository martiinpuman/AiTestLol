using System;
using System.Linq;

namespace Aurora.Platform.Tenancy.Catalog.Configuration;

/// <summary>
/// Renders an enum's names as the SQL list its column's check constraint allows, so the
/// constraint and the enum cannot disagree: adding a state means a new migration, which is the
/// visible, reviewable event it should be.
/// </summary>
internal static class StateNames
{
    public static string CheckConstraintSql<TState>(string columnName)
        where TState : struct, Enum
    {
        string names = string.Join(", ", Enum.GetNames<TState>().Select(name => $"'{name}'"));
        return $"{columnName} IN ({names})";
    }
}
