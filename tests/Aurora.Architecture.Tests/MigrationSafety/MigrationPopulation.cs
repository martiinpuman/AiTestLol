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
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Aurora.Architecture.Tests.MigrationSafety;

/// <summary>One command of a migration's generated <c>Up</c> script, as EF Core would send it to PostgreSQL.</summary>
/// <param name="CommandText">What MIG2 scans.</param>
/// <param name="TransactionSuppressed">
/// Captured for MIG4 (B-09.2), which nothing reads yet: ADR-0007 §7.2 rule 4 requires a
/// <c>CREATE INDEX CONCURRENTLY</c> to run outside the migration's transaction, and this flag is
/// how the runner knows to - Npgsql sets it from the <c>Npgsql:CreatedConcurrently</c> annotation,
/// and <c>Sql(…, suppressTransaction: true)</c> sets it for raw SQL.
/// </param>
/// <param name="Operation">
/// The <see cref="MigrationOperation"/> whose own generation produced exactly this command text,
/// or <c>null</c> when none did (ADR-0037 §3.2). An unattributed command is judged on its text
/// alone, which is the destructive direction.
/// </param>
internal sealed record GeneratedCommand(string CommandText, bool TransactionSuppressed, MigrationOperation? Operation);

/// <summary>A check constraint as EF's model snapshot declares it: schema, table and constraint name.</summary>
internal sealed record CheckConstraintReference(string? Schema, string Table, string Name)
{
    /// <summary>The table as the scanner names it: <c>schema.table</c>, lower-cased.</summary>
    public string TableKey => (Schema is null ? Table : Schema + "." + Table).ToLowerInvariant();
}

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

    /// <summary>This migration's target model, runtime-initialised; the next migration's source model.</summary>
    public IModel? TargetModel { get; init; }

    /// <summary>
    /// The check constraints of the schema this migration starts from: the target model of the
    /// migration immediately before it in the same assembly, by id (ADR-0037 §3.2). Empty for the
    /// first migration of an assembly. Read from EF's snapshot, never from a database, so a
    /// constraint created by raw SQL is never here and a drop of it is always destructive.
    /// </summary>
    public ImmutableArray<CheckConstraintReference> SourceCheckConstraints { get; init; } = [];
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
/// <b>Attribution (ADR-0037 §3.2).</b> Beside the batch generation, every operation is generated
/// on its own, and a batch command whose text one operation's own generation produced is
/// attributed to that operation. Batch generation stays authoritative for what is scanned;
/// per-operation generation answers only which operation produced a command. A command nothing
/// accounts for is unattributed, and MIG2 judges it on its text alone.
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
/// generate it either - and is reported, not skipped. The source model is EF's snapshot of the
/// previous migration, not the database: what raw SQL created, it does not know.
/// </para>
/// </remarks>
internal static class MigrationPopulation
{
    /// <summary>The EF Core base class every migration derives from, as its metadata name.</summary>
    public const string MigrationBaseType = "Microsoft.EntityFrameworkCore.Migrations.Migration";

    /// <summary>The provider name a migration's <c>Up()</c> runs under, so provider-specific builder extensions resolve.</summary>
    public const string ActiveProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";

    private static readonly Lazy<ImmutableArray<ScannedMigration>> LazyProduction = new(ReadProduction);

    /// <summary>Every migration declared by every project under <c>src/</c>, ordered by id, each knowing the schema it starts from.</summary>
    public static ImmutableArray<ScannedMigration> Production => LazyProduction.Value;

    /// <summary>
    /// The given migration types, read exactly as production ones are: ordered by id, and each
    /// handed the check constraints of the previous migration's model in the same assembly.
    /// </summary>
    public static ImmutableArray<ScannedMigration> Of(IEnumerable<Type> migrationTypes)
    {
        ImmutableArray<ScannedMigration> ordered =
            [.. migrationTypes.Select(Read).OrderBy(static migration => migration.Id, StringComparer.Ordinal)];

        ImmutableArray<ScannedMigration>.Builder withSources = ImmutableArray.CreateBuilder<ScannedMigration>(ordered.Length);
        var previousByAssembly = new Dictionary<string, ScannedMigration>(StringComparer.Ordinal);

        foreach (ScannedMigration migration in ordered)
        {
            ImmutableArray<CheckConstraintReference> source =
                previousByAssembly.TryGetValue(migration.AssemblyName, out ScannedMigration? previous)
                    ? CheckConstraintsOf(previous.TargetModel)
                    : [];

            withSources.Add(migration with { SourceCheckConstraints = source });
            previousByAssembly[migration.AssemblyName] = migration;
        }

        return withSources.ToImmutable();
    }

    public static ScannedMigration Read(Type migrationType)
    {
        string id = migrationType.GetCustomAttribute<MigrationAttribute>()?.Id ?? migrationType.FullName!;
        MigrationSafetyAttribute? safety = migrationType.GetCustomAttribute<MigrationSafetyAttribute>();
        string assemblyName = migrationType.Assembly.GetName().Name!;

        try
        {
            var migration = (Migration)Activator.CreateInstance(migrationType)!;
            migration.ActiveProvider = ActiveProvider;

            using var context = new SqlGenerationContext();
            IModel model = context.GetService<IModelRuntimeInitializer>().Initialize(migration.TargetModel);
            IMigrationsSqlGenerator generator = context.GetService<IMigrationsSqlGenerator>();

            return new ScannedMigration(
                id, migrationType.FullName!, assemblyName, safety, Generate(migration, model, generator), GenerationFailure: null)
            {
                TargetModel = model,
            };
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
    /// What <c>Migrator.GenerateUpSql</c> does - the operations, against the migration's own target
    /// model after runtime initialisation, through the provider's generator - plus the per-operation
    /// generation that attributes each batch command to the operation whose own output matches it.
    /// </summary>
    private static ImmutableArray<GeneratedCommand> Generate(Migration migration, IModel model, IMigrationsSqlGenerator generator)
    {
        IReadOnlyList<MigrationOperation> operations = migration.UpOperations;
        var producedBy = new Dictionary<string, MigrationOperation>(StringComparer.Ordinal);

        foreach (MigrationOperation operation in operations)
        {
            foreach (MigrationCommand own in generator.Generate([operation], model))
            {
                producedBy.TryAdd(own.CommandText, operation);
            }
        }

        return
        [
            .. generator.Generate(operations, model).Select(command => new GeneratedCommand(
                command.CommandText,
                command.TransactionSuppressed,
                producedBy.GetValueOrDefault(command.CommandText))),
        ];
    }

    private static ImmutableArray<CheckConstraintReference> CheckConstraintsOf(IModel? model) =>
        model is null
            ? []
            :
            [
                .. model.GetEntityTypes().SelectMany(entity =>
                    entity.GetCheckConstraints().Select(constraint => new CheckConstraintReference(
                        entity.GetSchema() ?? model.GetDefaultSchema(),
                        entity.GetTableName() ?? entity.ShortName(),
                        constraint.Name ?? constraint.ModelName))),
            ];

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
