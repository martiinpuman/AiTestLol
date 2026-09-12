using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text.Json;
using Aurora.Platform.Tenancy.Contracts;
using Aurora.SharedKernel;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Aurora.Platform.Tenancy.UnitTests.Kernel;

/// <summary>
/// The first half of ADR-0007 §4's guarantee, as ADR-0027 §1 restates it: neither proof of tenant
/// identity is constructible outside <c>Aurora.Platform.Tenancy</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The chain, link by link.</b> "No module can fabricate a <c>TenantScope</c>" (ADR-0007 §3.4)
/// rests on five links, and each has its own test here because a chain is only as good as the
/// link nobody read:
/// </para>
/// <list type="number">
/// <item><description>Every constructor of <c>TenantAccess</c> and <c>TenantScope</c> is
/// <c>internal</c>, and <c>TenantScope</c> is sealed, so no derived type can add a public one.</description></item>
/// <item><description><c>internal</c> reaches exactly the assemblies <c>[InternalsVisibleTo]</c>
/// names, so that list is asserted as an exact set - a grant added later is a red test, not a
/// silent widening.</description></item>
/// <item><description>No public member of either friend assembly that ships - the contracts
/// assembly and <c>Aurora.Platform.Tenancy</c> - mentions a <c>TenantAccess</c> anywhere in its
/// signature, and no public type of either inherits one through its base chain or interfaces,
/// unless named here by exact key as a sanctioned door, a proof-taking member, or one of the proof
/// types themselves. Direction is not inferred and declared members are not the whole population,
/// because inferring and declaring is where two reviews found the gaps. The scan is proven against
/// <see cref="ProofDoorProbes"/>, and <see cref="ProofMentionScan"/> states what a signature scan
/// cannot see.</description></item>
/// <item><description>There is no parameterless constructor at any accessibility. That is what
/// closes <c>new T()</c>, <c>Activator.CreateInstance</c>, System.Text.Json and
/// <c>DataContractSerializer</c> - each is tried, not reasoned about.</description></item>
/// <item><description>The one route no accessibility rule can close,
/// <c>RuntimeHelpers.GetUninitializedObject</c>, yields a scope that reads as absent on every
/// public property - every one, by reflection and counted, and a property that claims an absent
/// reading must actually be read: a getter that throws instead has its claim untested and fails,
/// and <c>NullReferenceException</c> fails by name because it is the signature of a getter
/// dereferencing what the constructor would have set. The last link is a scope every consumer
/// refuses, not one that passes for real.</description></item>
/// </list>
/// <para>
/// <b>What this file does not claim:</b> that reflection is blocked. Any code with reflection
/// permission can invoke an internal constructor; .NET has no mechanism against that and this
/// project does not pretend to one. The guarantee is a compile-time one (ADR-0007 §12.3), and
/// these tests examine the compiled metadata the compiler enforces.
/// </para>
/// </remarks>
public sealed class TenantAccessConstructionTests(ITestOutputHelper output)
{
    private const BindingFlags EveryInstanceConstructor =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>
    /// The floor under link 3's member count: what the scan measured on this branch, rounded down
    /// to the nearest ten, the same convention <c>scripts/verify.sh</c> applies to stage 6. Not the
    /// exact count - that makes every added member an edit here - but close enough that the six
    /// types carrying the proof (49 members between them) cannot drop out of the scan unnoticed.
    /// The second friend assembly contributes one member, so it can drop out under the floor; the
    /// named-type assertion below is what catches that, and both are kept for their own mode.
    /// Re-round whenever a type joins or leaves either friend assembly.
    /// </summary>
    private const int ExaminedFloor = 140;

    private static readonly Assembly Contracts = typeof(TenantAccess).Assembly;

    /// <summary>
    /// The assemblies <c>internal</c> reaches that ship: link 2's grants minus the test assembly.
    /// <c>Aurora.Platform.Tenancy</c> is where B-06.3 puts the scope factory, which is why its
    /// surface is scanned with the same allow-lists rather than trusted to stay internal.
    /// </summary>
    private static readonly Assembly[] ShippingFriends =
    [
        Contracts,
        typeof(TenantIdentityStamp).Assembly,
    ];

    /// <summary>
    /// Public members allowed to hand a proof out: the sanctioned doors, keyed by
    /// <see cref="ProofMentionScan.MemberKey"/> - type, member, generic arity and parameter list,
    /// so that sanctioning one overload sanctions only that overload. Empty today. B-06.3 adds
    /// <c>ITenantScopeFactory.OpenAsync(...)</c> here, with its parameters, and nothing else ever should.
    /// </summary>
    private static readonly HashSet<string> SanctionedDoors = new(StringComparer.Ordinal);

    /// <summary>
    /// Public members allowed to take a proof in, by value - ADR-0007 §4.5's shape - by the same
    /// key. Empty today. B-06.3 adds <c>ITenantDbContextFactory`1.CreateAsync(...)</c> and its
    /// siblings here. A member that takes a scope and hands it to a delegate is a door, not an
    /// inbound member, and belongs in <see cref="SanctionedDoors"/> or nowhere.
    /// </summary>
    private static readonly HashSet<string> ProofTakingMembers = new(StringComparer.Ordinal);

    /// <summary>
    /// The proof types themselves, which the inheritance check reports because each derives from
    /// <c>TenantAccess</c>, keyed by <see cref="ProofMentionScan.InheritedProofMention"/>. ADR-0027 §1
    /// names two: <c>TenantScope</c> today, <c>TenantDatabaseHandle</c> when its owning row lands.
    /// A third entry is a third proof type and needs an ADR before it needs a line here.
    /// </summary>
    private static readonly HashSet<string> ProofTypesThemselves = new(StringComparer.Ordinal)
    {
        "Aurora.Platform.Tenancy.Contracts.TenantScope : Aurora.Platform.Tenancy.Contracts.TenantAccess",
    };

    /// <summary>
    /// What each public property of <c>TenantScope</c> reads on a scope that skipped its
    /// constructor, when its type has a value that means "absent". A property named here claims
    /// what it reads; if its getter throws instead, the claim was not tested and the test fails.
    /// </summary>
    private static readonly Dictionary<string, Func<object?, bool>> ReadsAsAbsent = new(StringComparer.Ordinal)
    {
        // Backed by a plain field the constructor sets - not by the absence of a "disposed" flag,
        // which would make a hollow scope read as live, and not by a lease object the constructor
        // would have created, which would make a hollow scope throw NullReferenceException. This
        // is the reading a consumer checks first.
        ["IsActive"] = static value => value is false,
        ["TenantId"] = static value => value is TenantId { IsEmpty: true },
        ["TenantKey"] = static value => value is TenantKey { IsSpecified: false },
        ["ResidencyRegion"] = static value => value is Region { IsSpecified: false },
        ["SchemaVersion"] = static value => value is SchemaVersion { IsSpecified: false },
        ["Packages"] = static value => value is null,
    };

    /// <summary>
    /// The properties whose type has no value meaning "absent" - an enum's default is its first
    /// member - with the value a hollow scope reads, named so that nobody reads it as evidence of
    /// a real scope.
    /// </summary>
    private static readonly Dictionary<string, object> CannotRefuse = new(StringComparer.Ordinal)
    {
        ["Reason"] = TenantAccessReason.Request,
    };

    /// <summary>
    /// The properties that refuse a hollow scope by throwing, with the exception each is designed
    /// to throw. Empty today: no property of <c>TenantScope</c> throws. B-06.3's lease may add one
    /// (<c>TenantScopeExpiredException</c>, ADR-0007 §10.4); a <c>NullReferenceException</c> is never
    /// a designed refusal and is refused by name whether or not a property is listed here.
    /// </summary>
    private static readonly Dictionary<string, Type> RefusesByThrowing = new(StringComparer.Ordinal);

    [Fact]
    public void TenantAccess_is_abstract_and_its_only_constructor_is_internal()
    {
        typeof(TenantAccess).IsAbstract.ShouldBeTrue();

        ConstructorInfo[] constructors = typeof(TenantAccess).GetConstructors(EveryInstanceConstructor);

        constructors.ShouldHaveSingleItem();
        ShouldBeInternal(constructors[0]);
    }

    [Fact]
    public void TenantScope_is_sealed_derives_from_TenantAccess_and_its_only_constructor_is_internal()
    {
        typeof(TenantScope).IsSealed.ShouldBeTrue(
            "a derived type could otherwise expose a public constructor that chains to the internal one");
        typeof(TenantScope).BaseType.ShouldBe(typeof(TenantAccess));
        typeof(IAsyncDisposable).IsAssignableFrom(typeof(TenantScope)).ShouldBeTrue("ADR-0007 §3.4: a scope is leased");

        ConstructorInfo[] constructors = typeof(TenantScope).GetConstructors(EveryInstanceConstructor);

        constructors.ShouldHaveSingleItem();
        ShouldBeInternal(constructors[0]);
        constructors[0].GetParameters().Select(parameter => parameter.ParameterType).ShouldBe(
            [
                typeof(TenantId), typeof(TenantKey), typeof(Region), typeof(SchemaVersion),
                typeof(InstalledPackages), typeof(TenantAccessReason),
            ],
            "the constructor takes every fact ADR-0007 §3.4 lists, so a scope missing one cannot be built");
    }

    [Fact]
    public void Internals_are_visible_to_the_tenancy_assembly_and_its_unit_test_assembly_and_nothing_else()
    {
        // Link 2. "internal" means nothing on its own: it means "these assemblies". ADR-0007 §3.4
        // names the tenancy assembly and its test assembly as the only grants, and this asserts the
        // exact set rather than a floor, so the day a third name appears the test says so.
        string[] grants =
        [
            .. Contracts.GetCustomAttributes<InternalsVisibleToAttribute>()
                .Select(attribute => attribute.AssemblyName)
                .OrderBy(name => name, StringComparer.Ordinal),
        ];

        grants.ShouldBe(["Aurora.Platform.Tenancy", "Aurora.Platform.Tenancy.UnitTests"]);
    }

    [Fact]
    public void No_public_member_or_type_of_a_shipping_friend_assembly_mentions_a_proof_unless_it_is_named_here()
    {
        // Link 3. Every public member of every public type in both friend assemblies that ship,
        // nested types included, and every public type's inheritance. A mention is reported by
        // exact key and must be in one of the three allow-lists; every allow-list entry must match
        // exactly one mention, so a stale or misspelled entry sanctions nothing. The count is printed
        // on every run and held to a floor, and the types the rule is about are asserted as
        // visited - a floor proves the scan ran, naming the types proves what it ran over.
        ProofMentionScanResult scan = ProofMentionScan.Over(ShippingFriends.SelectMany(ProofMentionScan.TopLevelPublicTypes));
        string[] allowed = [.. SanctionedDoors, .. ProofTakingMembers, .. ProofTypesThemselves];

        output.WriteLine(
            $"link 3: {scan.Examined} public members examined over {scan.VisitedTypes.Count} public types in "
            + $"{Plural(ShippingFriends.Length, "assembly", "assemblies")} "
            + $"({string.Join(", ", ShippingFriends.Select(static assembly => assembly.GetName().Name))}); "
            + $"{scan.Mentions.Count} mention a proof, {allowed.Length} allow-listed; floor {ExaminedFloor}");

        scan.Examined.ShouldBeGreaterThanOrEqualTo(
            ExaminedFloor,
            $"{scan.Examined} public members examined; the floor is the measured count rounded down to the "
            + "nearest ten, so fewer means a type carrying the proof has dropped out of the scan");
        scan.VisitedTypes.ShouldContain(typeof(TenantAccess).FullName!);
        scan.VisitedTypes.ShouldContain(typeof(TenantScope).FullName!);
        scan.VisitedTypes.ShouldContain(
            "Aurora.Platform.Tenancy.CatalogServiceCollectionExtensions",
            "the one non-migration type Aurora.Platform.Tenancy exports; without it the scan read one assembly, not two");

        scan.Mentions
            .Where(mention => !allowed.Contains(mention, StringComparer.Ordinal))
            .ShouldBeEmpty(
                $"{scan.Examined} public members examined; a member whose signature mentions a TenantAccess "
                + "is either a door past the internal constructor (name it in SanctionedDoors) or an inbound "
                + "handler shape (name it in ProofTakingMembers), and a type that inherits one is a door unless "
                + "it is a proof type named in ProofTypesThemselves");
        foreach (string entry in allowed)
        {
            scan.Mentions.Count(mention => string.Equals(mention, entry, StringComparison.Ordinal)).ShouldBe(
                1,
                $"allow-list entry '{entry}' must match exactly one mention; a stale, misspelled or "
                + "overload-ambiguous entry sanctions nothing");
        }
    }

    [Fact]
    public void The_scan_reports_every_door_shape_and_the_inbound_shape_and_neither_negative_control()
    {
        // The scan proven to fail: run over a type built of doors, it must report each - including
        // the two shapes the first review walked through the earlier scan in green (the event and
        // the callback parameter) and the three types the second review walked through the next
        // one (collections that declare nothing and inherit their scopes) - and must stay silent on
        // the members that mention no proof. The examined count is exact so that a member the
        // fixture gained or lost is noticed too.
        ProofMentionScanResult scan = ProofMentionScan.Over([typeof(ProofDoorProbes)]);

        string probes = typeof(ProofDoorProbes).FullName!;
        string nested = typeof(ProofDoorProbes.Nested).FullName!;
        string scope = typeof(TenantScope).ToString();
        string scopeCallback = typeof(Action<TenantScope>).ToString();
        Type[] listAncestry =
        [
            typeof(List<TenantScope>), typeof(IList<TenantScope>), typeof(ICollection<TenantScope>),
            typeof(IEnumerable<TenantScope>), typeof(IReadOnlyList<TenantScope>), typeof(IReadOnlyCollection<TenantScope>),
        ];

        scan.VisitedTypes.ShouldBe(
            [
                probes,
                typeof(ProofDoorProbes.DeeperCollection).FullName!,
                typeof(ProofDoorProbes.ExplicitScopeCollection).FullName!,
                nested,
                typeof(ProofDoorProbes.OpenScopeCollection).FullName!,
            ],
            "every nested public type is expanded");
        scan.Examined.ShouldBe(
            21,
            "the fixture's public members: one event with two accessors, nine methods, three properties "
            + "with five accessors between them, the nested type's property with its getter, and the "
            + "explicit collection's ToString");
        scan.Mentions.Order(StringComparer.Ordinal).ShouldBe(
            new[]
            {
                Inherits(typeof(ProofDoorProbes.DeeperCollection), listAncestry),
                Inherits(typeof(ProofDoorProbes.ExplicitScopeCollection), typeof(IReadOnlyCollection<TenantScope>), typeof(IEnumerable<TenantScope>)),
                nested + ".Held",
                nested + ".get_Held()",
                Inherits(typeof(ProofDoorProbes.OpenScopeCollection), listAncestry),
                probes + ".Current",
                probes + ".OnScope(" + typeof(ScopeCallback) + ")",
                probes + ".OpenAllAsync()",
                probes + ".OpenAs<T>()",
                probes + ".OpenWithVersion()",
                probes + ".Raise(" + scope + ")",
                probes + ".ScopeOpened",
                probes + ".TryOpen(" + scope + "&)",
                probes + ".WithScope(" + scopeCallback + ")",
                probes + ".add_ScopeOpened(" + scopeCallback + ")",
                probes + ".get_Current()",
                probes + ".remove_ScopeOpened(" + scopeCallback + ")",
                probes + ".set_Current(" + scope + ")",
            }.Order(StringComparer.Ordinal));
        scan.Mentions.ShouldNotContain(probes + ".Version");
        scan.Mentions.ShouldNotContain(probes + ".get_Version()");
        scan.Mentions.ShouldNotContain(probes + ".Describe(System.String)");
        scan.Mentions.ShouldNotContain(probes + ".All()", "the member names no proof; its return type is reported as a type");
        scan.Mentions.ShouldNotContain(probes + ".Bag()", "the member names no proof; its return type is reported as a type");
    }

    [Fact]
    public void No_parameterless_constructor_exists_at_any_accessibility()
    {
        // Link 4. `new T()` requires a public parameterless constructor and Activator's non-public
        // overload settles for any; neither exists, so both routes end in the compiler or in
        // MissingMethodException rather than in a scope.
        typeof(TenantScope).GetConstructor(EveryInstanceConstructor, Type.EmptyTypes).ShouldBeNull();

        Should.Throw<MissingMethodException>(() => Activator.CreateInstance(typeof(TenantScope), nonPublic: true));
    }

    [Fact]
    public void System_Text_Json_cannot_deserialise_a_scope()
    {
        // Link 4, serializer form. A scope is never serialised (ADR-0007 §10.4), and the inverse is
        // closed too: with no parameterless constructor and no public parameterised one, the
        // serializer has nothing to call. The payload names every property so that the refusal is
        // about the type's shape, not about missing data.
        const string payload =
            """
            {"TenantId":"019230b0-5c6a-7c3e-9a2f-0f1e2d3c4b5a","TenantKey":"acme","ResidencyRegion":"nz",
             "SchemaVersion":1,"Packages":[],"Reason":0,"IsActive":true}
            """;

        Should.Throw<NotSupportedException>(() => JsonSerializer.Deserialize<TenantScope>(payload));
    }

    [Fact]
    public void DataContractSerializer_cannot_deserialise_a_scope()
    {
        // Link 4, the other in-box serializer. It builds a POCO contract around a parameterless
        // constructor of any accessibility, and there is none. The contract is built on first use,
        // not on construction, so the refusal is asserted on an actual read of a document that
        // names the type and its properties.
        const string document =
            """
            <TenantScope xmlns="http://schemas.datacontract.org/2004/07/Aurora.Platform.Tenancy.Contracts">
              <IsActive>true</IsActive>
              <Reason>Request</Reason>
            </TenantScope>
            """;
        DataContractSerializer serializer = new(typeof(TenantScope));
        using System.Xml.XmlReader reader = System.Xml.XmlReader.Create(new System.IO.StringReader(document));

        Should.Throw<InvalidDataContractException>(() => serializer.ReadObject(reader));
    }

    [Fact]
    public void A_scope_that_skipped_its_constructor_reads_as_absent_on_every_public_property()
    {
        // Link 5. GetUninitializedObject allocates the object and runs no constructor; nothing about
        // accessibility can stop it. What stops it being useful is that every public property reads
        // as absent - and "every" is by reflection, not by hand: a property added to TenantScope
        // (B-06.3's lease, ADR-0029 A1.2's company scope) fails this test until it is shown absent
        // here, named as one whose type cannot refuse, or named as refusing by throwing a designed
        // exception. A property whose default read as permissive would otherwise make a hollow
        // scope more privileged than a real one, in green.
        //
        // A property that claims an absent reading is read, and its reading is judged. A throw from
        // it is not a pass: the claim went untested, so it fails. And NullReferenceException fails
        // by name whatever the property is listed as, because on a hollow scope it is the signature
        // of a getter dereferencing a field the constructor would have set - B-06.3's most likely
        // lease shape, IsActive => _lease.Held - and not a refusal anyone designed.
        object hollow = RuntimeHelpers.GetUninitializedObject(typeof(TenantScope));
        PropertyInfo[] properties = typeof(TenantScope).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        properties.Length.ShouldBe(
            ReadsAsAbsent.Count + CannotRefuse.Count + RefusesByThrowing.Count,
            "a property added to TenantScope must be shown to read as absent on a hollow scope, "
            + "named in CannotRefuse with the value it reads, or named in RefusesByThrowing with the "
            + "exception it is designed to throw");
        properties.Select(static property => property.Name).Order(StringComparer.Ordinal).ShouldBe(
            ReadsAsAbsent.Keys.Concat(CannotRefuse.Keys).Concat(RefusesByThrowing.Keys).Order(StringComparer.Ordinal));

        List<string> report = [];
        foreach (PropertyInfo property in properties)
        {
            object? reading;
            try
            {
                reading = property.GetValue(hollow);
            }
            catch (TargetInvocationException refused)
            {
                Exception thrown = refused.InnerException ?? refused;
                report.Add($"{property.Name}: threw {thrown.GetType().Name}");

                thrown.ShouldNotBeOfType<NullReferenceException>(
                    $"{property.Name} threw NullReferenceException on a hollow scope: a getter dereferencing "
                    + "something the constructor would have set, which is the bug link 5 exists to catch, not a "
                    + "designed refusal");
                RefusesByThrowing.TryGetValue(property.Name, out Type? designed).ShouldBeTrue(
                    $"{property.Name} is named as reading a value, but its getter threw {thrown.GetType().Name} "
                    + "on a hollow scope, so nothing checked what it reads; a property that refuses by throwing "
                    + "must be named in RefusesByThrowing with the exception it throws");
                thrown.GetType().ShouldBe(
                    designed,
                    $"{property.Name} is named as refusing with {designed!.Name} but threw {thrown.GetType().Name}");
                continue;
            }

            report.Add($"{property.Name}: {reading ?? "null"}");
            if (ReadsAsAbsent.TryGetValue(property.Name, out Func<object?, bool>? absent))
            {
                absent(reading).ShouldBeTrue($"{property.Name} read '{reading}' on a hollow scope, which is not an absent value");
            }
            else if (CannotRefuse.TryGetValue(property.Name, out object? unavoidable))
            {
                reading.ShouldBe(unavoidable, $"{property.Name} cannot refuse; it reads its type's default");
            }
            else
            {
                RefusesByThrowing.ContainsKey(property.Name).ShouldBeFalse(
                    $"{property.Name} is named as refusing by throwing {RefusesByThrowing.GetValueOrDefault(property.Name)?.Name}, "
                    + $"but read '{reading}' instead");
            }
        }

        report.Count.ShouldBe(properties.Length, string.Join("; ", report));
    }

    [Fact]
    public void The_abstract_base_cannot_be_materialised_at_all()
    {
        Should.Throw<MemberAccessException>(() => RuntimeHelpers.GetUninitializedObject(typeof(TenantAccess)));
    }

    private static string Inherits(Type type, params Type[] ancestry) =>
        type.FullName + " : " + string.Join(", ", ancestry.Select(static ancestor => ancestor.ToString()).Order(StringComparer.Ordinal));

    private static string Plural(int count, string one, string many) => $"{count} {(count == 1 ? one : many)}";

    private static void ShouldBeInternal(ConstructorInfo constructor)
    {
        constructor.IsAssembly.ShouldBeTrue("internal: reachable only through [InternalsVisibleTo]");
        constructor.IsPublic.ShouldBeFalse();
        constructor.IsFamily.ShouldBeFalse("protected would let a derived type outside the grant chain to it");
        constructor.IsFamilyOrAssembly.ShouldBeFalse("protected internal is protected");
    }
}
