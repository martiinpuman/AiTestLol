using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Aurora.Architecture.Tests.Metadata;
using Aurora.Architecture.Tests.Rules;
using Aurora.Architecture.Tests.Solution;
using Aurora.Platform.Tenancy.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Aurora.Architecture.Tests.MigrationSafety;

/// <summary>One command of a migration's generated <c>Up</c> script, as EF Core would send it to PostgreSQL.</summary>
internal sealed record GeneratedCommand(string CommandText, bool TransactionSuppressed);

/// <summary>
/// One migration and the SQL its <c>Up()</c> generates for PostgreSQL - or the reason that SQL
/// could not be generated, in which case nothing about the migration is known.
/// </summary>
/// <param name="Id">The EF migration id (<c>[Migration("…")]</c>), or the type's full name when there is none.</param>
/// <param name="TypeFullName">The migration class, which is what a violation names.</param>
/// <param name="AssemblyName">The production assembly that declares it.</param>
/// <param name="Safety">The <c>[MigrationSafety]</c> annotation, or <c>null</c> when the migration carries none.</param>
/// <param name="Commands">The generated <c>Up</c> script, one entry per command; empty when generation failed.</param>
/// <param name="GenerationFailure"><c>null</c> when <paramref name="Commands"/> is the generated script.</param>
internal sealed record ScannedMigration(
    string Id,
    string TypeFullName,
    string AssemblyName,
    MigrationSafetyAttribute? Safety,
    ImmutableArray<GeneratedCommand> Commands,
    string? GenerationFailure)
{
    public MigrationCategory? Category => Safety?.Category;

    /// <summary>How the category reads in a message.</summary>
    public string CategoryDisplay => Category?.ToString() ?? "unannotated";
}

/// <summary>
/// Every EF migration in production, with its SQL generated the way the migration runner would
/// generate it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the SQL is generated rather than read.</b> A migration is C#; the SQL that runs against
/// a tenant is what the provider emits from its <c>UpOperations</c>. <c>DropColumn(…)</c> contains
/// no string saying <c>DROP COLUMN</c> anywhere, so a scan over <c>migrationBuilder.Sql(…)</c>
/// arguments would report clean on a migration that drops a column. ADR-0007 §7.2 rule 2 says
/// "generates each migration's SQL", and that is the link this class follows: instantiate the
/// migration, run <c>Up()</c> under the Npgsql provider name, and hand the operations to Npgsql's
/// own <see cref="IMigrationsSqlGenerator"/>. What comes back is what the runner would execute,
/// raw strings and generated statements alike, one <see cref="GeneratedCommand"/> per command.
/// </para>
/// <para>
/// <b>How the population is found.</b> From <see cref="SolutionLayout.ProductionTypes"/> - the
/// same unfiltered every-project-under-<c>src/</c> population every other rule uses - every
/// non-abstract type whose base chain reaches <see cref="MigrationBaseType"/>. Only then is the
/// assembly loaded, by path, into this process; a production assembly with no migration is never
/// loaded. Loading resolves the assembly's own references from this test's output directory
/// first, where EF Core, Npgsql, the kernel and the tenancy contracts already are, so the
/// <c>[MigrationSafety]</c> a production migration carries is the same type this project
/// references and reads it back with.
/// </para>
/// <para>
/// <b>What it cannot see, stated.</b> A migration that cannot be instantiated, whose <c>Up()</c>
/// throws, or whose operations the generator refuses is not dropped from the population: it is
/// kept with <see cref="ScannedMigration.GenerationFailure"/> set and no commands, and MIG2 reports
/// it. A hand-written data operation (<c>InsertData</c>, <c>UpdateData</c>, <c>DeleteData</c>)
/// that names no column types in a migration with no target model is such a failure - EF cannot
/// generate it either - and is reported, not skipped.
/// </para>
/// </remarks>
internal static class MigrationPopulation
{
    /// <summary>The EF Core base class every migration derives from, as its metadata name.</summary>
    public const string MigrationBaseType = "Microsoft.EntityFrameworkCore.Migrations.Migration";

    /// <summary>The provider name a migration's <c>Up()</c> runs under, so provider-specific builder extensions resolve.</summary>
    public const string ActiveProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";

    private static readonly Lazy<ImmutableArray<ScannedMigration>> LazyProduction = new(ReadProduction);

    /// <summary>Every migration declared by every project under <c>src/</c>, ordered by id.</summary>
    public static ImmutableArray<ScannedMigration> Production => LazyProduction.Value;

    /// <summary>The given migration types, read exactly as production ones are.</summary>
    public static ImmutableArray<ScannedMigration> Of(IEnumerable<Type> migrationTypes) =>
        [.. migrationTypes.Select(Read).OrderBy(static migration => migration.Id, StringComparer.Ordinal)];

    public static ScannedMigration Read(Type migrationType)
    {
        string id = migrationType.GetCustomAttribute<MigrationAttribute>()?.Id ?? migrationType.FullName!;
        MigrationSafetyAttribute? safety = migrationType.GetCustomAttribute<MigrationSafetyAttribute>();
        string assemblyName = migrationType.Assembly.GetName().Name!;

        try
        {
            var migration = (Migration)Activator.CreateInstance(migrationType)!;
            migration.ActiveProvider = ActiveProvider;

            return new ScannedMigration(
                id, migrationType.FullName!, assemblyName, safety, Generate(migration), GenerationFailure: null);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            // Not swallowed: the failure is the migration's report, and MIG2 turns it into a
            // violation. Activator wraps whatever Up() threw; the inner one is the reason.
            Exception cause = failure is TargetInvocationException { InnerException: { } inner } ? inner : failure;

            return new ScannedMigration(
                id, migrationType.FullName!, assemblyName, safety, Commands: [], $"{cause.GetType().Name}: {cause.Message}");
        }
    }

    /// <summary>
    /// What <c>Migrator.GenerateUpSql</c> does: the operations, against the migration's own target
    /// model (the Designer's <c>BuildTargetModel</c>) after runtime initialization, through the
    /// provider's generator. Data operations without column types resolve them from that model, so
    /// a scaffolded <c>InsertData</c> generates the same here as in the runner.
    /// </summary>
    private static ImmutableArray<GeneratedCommand> Generate(Migration migration)
    {
        using var context = new SqlGenerationContext();
        IModel model = context.GetService<IModelRuntimeInitializer>().Initialize(migration.TargetModel);
        IMigrationsSqlGenerator generator = context.GetService<IMigrationsSqlGenerator>();

        return
        [
            .. generator.Generate(migration.UpOperations, model)
                .Select(static command => new GeneratedCommand(command.CommandText, command.TransactionSuppressed)),
        ];
    }

    private static ImmutableArray<ScannedMigration> ReadProduction()
    {
        TypeIndex index = TypeIndex.Of(SolutionLayout.ProductionTypes);

        ImmutableArray<ScannedType> migrationTypes =
        [
            .. index.All.Where(type =>
                (type.Attributes & TypeAttributes.Abstract) == 0 && index.DerivesFrom(type, MigrationBaseType)),
        ];

        List<Type> loaded = [];

        foreach (IGrouping<string, ScannedType> group in migrationTypes.GroupBy(static type => type.AssemblyName, StringComparer.Ordinal))
        {
            ScannedAssembly scanned = SolutionLayout.ProductionAssemblies.Single(assembly =>
                string.Equals(assembly.Name, group.Key, StringComparison.Ordinal));
            Assembly assembly = Load(scanned);

            loaded.AddRange(group.Select(type => assembly.GetType(type.FullName, throwOnError: true)!));
        }

        return Of(loaded);
    }

    private static Assembly Load(ScannedAssembly scanned)
    {
        Assembly? alreadyLoaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly =>
            string.Equals(assembly.GetName().Name, scanned.Name, StringComparison.Ordinal));

        return alreadyLoaded ?? Assembly.LoadFrom(scanned.Path);
    }

    /// <summary>
    /// A context configured for Npgsql and nothing else, so its service provider hands out the
    /// provider's SQL generator. Never connects: no connection string, no model, no query.
    /// </summary>
    private sealed class SqlGenerationContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) => optionsBuilder.UseNpgsql();
    }
}
