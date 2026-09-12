using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Aurora.Platform.Tenancy.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Aurora.Platform.Tenancy.IntegrationTests.Routing;

/// <summary>
/// Records every statement a catalog context sends to PostgreSQL. The instrument behind every
/// "did the resolver read the catalog" assertion in this folder: a statement recorded here left
/// the process, so a count that did not move is a read that never happened.
/// </summary>
internal sealed class CatalogStatementCounter : DbCommandInterceptor
{
    private readonly ConcurrentQueue<string> _statements = new();

    public int Count => _statements.Count;

    public IReadOnlyList<string> Statements => [.. _statements];

    /// <summary>A catalog context over <paramref name="connectionString"/> whose every statement this counter sees.</summary>
    internal CatalogDbContext Open(string connectionString)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>();
        CatalogDbContextOptions.Configure(options, connectionString);
        options.AddInterceptors(this);
        return new CatalogDbContext(options.Options);
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        _statements.Enqueue(command.CommandText);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        _statements.Enqueue(command.CommandText);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}
