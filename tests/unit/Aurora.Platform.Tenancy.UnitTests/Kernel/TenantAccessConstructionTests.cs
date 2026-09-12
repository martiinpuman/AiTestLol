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
/// <item><description>Nothing public in the contracts assembly returns a <c>TenantAccess</c>, so
/// there is no factory to call instead of the constructor. When B-06.3 declares
/// <c>ITenantScopeFactory</c>, that one door is added to <see cref="SanctionedDoors"/> by name.</description></item>
/// <item><description>There is no parameterless constructor at any accessibility. That is what
/// closes <c>new T()</c>, <c>Activator.CreateInstance</c>, System.Text.Json and
/// <c>DataContractSerializer</c> - each is tried, not reasoned about.</description></item>
/// <item><description>The one route no accessibility rule can close,
/// <c>RuntimeHelpers.GetUninitializedObject</c>, yields a scope that reports itself unusable on
/// every property. The last link is a scope every consumer refuses, not one that passes for real.</description></item>
/// </list>
/// <para>
/// <b>What this file does not claim:</b> that reflection is blocked. Any code with reflection
/// permission can invoke an internal constructor; .NET has no mechanism against that and this
/// project does not pretend to one. The guarantee is a compile-time one (ADR-0007 §12.3), and
/// these tests examine the compiled metadata the compiler enforces.
/// </para>
/// </remarks>
public sealed class TenantAccessConstructionTests
{
    private const BindingFlags EveryInstanceConstructor =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private const BindingFlags EveryPublicMember =
        BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    private static readonly Assembly Contracts = typeof(TenantAccess).Assembly;

    /// <summary>
    /// Public members that are allowed to hand out a tenant proof: the sanctioned doors. Empty
    /// today. B-06.3 adds <c>ITenantScopeFactory.OpenAsync</c> here, and nothing else ever should.
    /// </summary>
    private static readonly HashSet<string> SanctionedDoors = new(StringComparer.Ordinal);

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
    public void Nothing_public_in_the_contracts_assembly_hands_out_a_tenant_access()
    {
        // Link 3. A public static factory, a public property, an out parameter, a Task<TenantScope>
        // somewhere - any of them is a door that makes the internal constructor irrelevant. Every
        // public member of every public type is read, including generic arguments and by-ref
        // parameters, and the count is asserted so this cannot pass by examining nothing.
        List<string> doors = [];
        int examined = 0;

        foreach (Type type in Contracts.GetExportedTypes())
        {
            foreach (MemberInfo member in type.GetMembers(EveryPublicMember))
            {
                if (member is ConstructorInfo)
                {
                    continue;
                }

                examined++;
                if (HandsOutTenantAccess(member))
                {
                    doors.Add(type.FullName + "." + member.Name);
                }
            }
        }

        examined.ShouldBeGreaterThan(60, "the contracts assembly's public surface is dozens of members; fewer means the scan missed it");
        doors.Where(door => !SanctionedDoors.Contains(door)).ShouldBeEmpty(
            $"{examined} public members examined; a member returning or exposing a TenantAccess is a "
            + "door past the internal constructor and must be named in SanctionedDoors");
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
    public void A_scope_that_skipped_its_constructor_is_inert_on_every_property_it_can_be_asked()
    {
        // Link 5. GetUninitializedObject allocates the object and runs no constructor; nothing about
        // accessibility can stop it. What stops it being useful is that every fact the constructor
        // would have checked reads as absent: the scope says it is not active, names no tenant, no
        // key, no region and no schema version, and carries no package set. A consumer that reads
        // any of them fails closed. IsActive is the one to read first, and it is backed by a plain
        // field the constructor sets - not by the absence of a "disposed" flag, which would make a
        // hollow scope read as live.
        object hollow = RuntimeHelpers.GetUninitializedObject(typeof(TenantScope));

        TenantScope scope = hollow.ShouldBeOfType<TenantScope>();
        scope.IsActive.ShouldBeFalse();
        scope.TenantId.IsEmpty.ShouldBeTrue();
        scope.TenantKey.IsSpecified.ShouldBeFalse();
        scope.ResidencyRegion.IsSpecified.ShouldBeFalse();
        scope.SchemaVersion.IsSpecified.ShouldBeFalse();
        scope.Packages.ShouldBeNull();

        // The one property that cannot refuse: an enum's default is its first member, Request.
        // Named here so nobody reads Reason as evidence of a real scope.
        scope.Reason.ShouldBe(TenantAccessReason.Request);
    }

    [Fact]
    public void The_abstract_base_cannot_be_materialised_at_all()
    {
        Should.Throw<MemberAccessException>(() => RuntimeHelpers.GetUninitializedObject(typeof(TenantAccess)));
    }

    private static void ShouldBeInternal(ConstructorInfo constructor)
    {
        constructor.IsAssembly.ShouldBeTrue("internal: reachable only through [InternalsVisibleTo]");
        constructor.IsPublic.ShouldBeFalse();
        constructor.IsFamily.ShouldBeFalse("protected would let a derived type outside the grant chain to it");
        constructor.IsFamilyOrAssembly.ShouldBeFalse("protected internal is protected");
    }

    private static bool HandsOutTenantAccess(MemberInfo member) => member switch
    {
        MethodInfo method => Mentions(method.ReturnType)
            || method.GetParameters().Any(parameter => parameter.ParameterType.IsByRef && Mentions(parameter.ParameterType)),
        PropertyInfo property => Mentions(property.PropertyType),
        FieldInfo field => Mentions(field.FieldType),
        _ => false,
    };

    private static bool Mentions(Type type)
    {
        if (type.IsByRef || type.IsArray || type.IsPointer)
        {
            return Mentions(type.GetElementType()!);
        }

        if (typeof(TenantAccess).IsAssignableFrom(type))
        {
            return true;
        }

        return type.IsGenericType && type.GetGenericArguments().Any(Mentions);
    }
}
