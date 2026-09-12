using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Shouldly;
using Xunit;

namespace Aurora.Countries.Contracts.UnitTests;

/// <summary>
/// The approved-API snapshot of ADR-0008 §3.1: the public surface of
/// <c>Aurora.Countries.Contracts</c> is a versioned contract, and it may not change by accident.
/// </summary>
/// <remarks>
/// <para>
/// The gate itself is <c>Microsoft.CodeAnalysis.PublicApiAnalyzers</c>: any public member missing
/// from <c>PublicAPI.Shipped.txt</c> fails the build with RS0016, and
/// <c>scripts/approve-contract-api.sh</c> is the deliberate step that puts it there — which is the
/// moment a human decides the SemVer bump.
/// </para>
/// <para>
/// These tests exist because that gate lives in a <c>PackageReference</c> and an
/// <c>AdditionalFiles</c> item, and both can be deleted. They compare the snapshot against the
/// assembly as it actually is, so a contract that quietly grows a type, or a snapshot that keeps a
/// line for a type that no longer exists, fails here whether or not the analyzer is still in the
/// build. Both report the number of entries they compared: a snapshot test that matched nothing
/// would otherwise pass exactly like one that matched everything.
/// </para>
/// </remarks>
public sealed class ContractPublicApiTests
{
    private static readonly IReadOnlyList<string> SnapshotLines = ReadSnapshot();

    [Fact]
    public void The_snapshot_names_every_public_type_in_the_contract()
    {
        HashSet<string> declared = PublicTypeNames();
        HashSet<string> approved = ApprovedTypeNames();

        declared.Count.ShouldBeGreaterThan(50,
            "the contract has ten extension points and their supporting types; a reflection pass " +
            "that found almost nothing has failed rather than passed");

        string[] unapproved = [.. declared.Except(approved).Order(StringComparer.Ordinal)];

        unapproved.ShouldBeEmpty(
            $"these public types are not in PublicAPI.Shipped.txt. Adding a type to the contract is " +
            $"a MINOR core-contract bump (ADR-0008 §3.1): run scripts/approve-contract-api.sh and " +
            $"read the diff. Compared {declared.Count} declared type(s) against {approved.Count} " +
            $"approved one(s).");
    }

    [Fact]
    public void The_snapshot_holds_no_line_for_a_type_that_no_longer_exists()
    {
        HashSet<string> declared = PublicTypeNames();
        HashSet<string> approved = ApprovedTypeNames();

        string[] stale = [.. approved.Except(declared).Order(StringComparer.Ordinal)];

        stale.ShouldBeEmpty(
            $"PublicAPI.Shipped.txt names these types, which the assembly no longer has. Removing a " +
            $"type from the contract is a MAJOR core-contract bump and reaches every package in the " +
            $"fleet. Compared {approved.Count} approved type(s) against {declared.Count} declared " +
            $"one(s).");
    }

    /// <summary>
    /// Members are the analyzer's business, not this test's — but the file has to look like a
    /// populated snapshot rather than a header and three lines, or the two tests above would pass
    /// over almost nothing.
    /// </summary>
    [Fact]
    public void The_snapshot_records_members_and_not_only_types()
    {
        int members = SnapshotLines.Count(line => line.Contains(" -> ", StringComparison.Ordinal));

        members.ShouldBeGreaterThan(300,
            $"PublicAPI.Shipped.txt holds {members} member line(s) over " +
            $"{SnapshotLines.Count} line(s) in total, which is not the shape of a snapshot of this " +
            $"contract");
    }

    [Fact]
    public void The_snapshot_declares_nullable_tracking_so_annotations_are_part_of_the_contract() =>
        SnapshotLines.ShouldContain("#nullable enable");

    private static HashSet<string> ApprovedTypeNames() =>
        new HashSet<string>(
            SnapshotLines.Where(line =>
                !line.StartsWith('#')
                && !line.Contains(" -> ", StringComparison.Ordinal)
                && !line.Contains('(', StringComparison.Ordinal)),
            StringComparer.Ordinal);

    private static HashSet<string> PublicTypeNames() =>
        new HashSet<string>(
            typeof(CoreContract).Assembly.GetExportedTypes().Select(ApiName),
            StringComparer.Ordinal);

    /// <summary>The type's name in the analyzer's own shape: namespace-qualified, generics spelled out.</summary>
    private static string ApiName(Type type)
    {
        StringBuilder name = new();

        if (type.DeclaringType is not null)
        {
            name.Append(ApiName(type.DeclaringType)).Append('.');
        }
        else if (!string.IsNullOrEmpty(type.Namespace))
        {
            name.Append(type.Namespace).Append('.');
        }

        string simple = type.Name;
        int arity = simple.IndexOf('`', StringComparison.Ordinal);
        if (arity < 0)
        {
            return name.Append(simple).ToString();
        }

        name.Append(simple, 0, arity)
            .Append('<')
            .Append(string.Join(", ", type.GetGenericArguments().Select(argument => argument.Name)))
            .Append('>');

        return name.ToString();
    }

    private static IReadOnlyList<string> ReadSnapshot()
    {
        using Stream resource =
            typeof(ContractPublicApiTests).Assembly.GetManifestResourceStream("PublicAPI.Shipped.txt")
            ?? throw new InvalidOperationException(
                "PublicAPI.Shipped.txt is not embedded in the test assembly, so the snapshot cannot " +
                "be compared. It is linked in the .csproj from the contracts project.");

        using StreamReader reader = new(resource, Encoding.UTF8);
        return [.. reader.ReadToEnd()
            .ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }
}
