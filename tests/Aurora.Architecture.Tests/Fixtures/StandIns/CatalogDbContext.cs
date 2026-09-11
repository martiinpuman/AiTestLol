using Microsoft.EntityFrameworkCore;

// This namespace is deliberate: it is B-05's, not this test project's. See the remarks.
namespace Aurora.Platform.Tenancy.Catalog;

/// <summary>
/// A stand-in for B-05's catalog context, compiled at its exact full name inside this test
/// assembly.
/// </summary>
/// <remarks>
/// <para>
/// ADR-0032 §4.3 exempts exactly one (full name, assembly) pair from the tenant-context rules:
/// <c>Aurora.Platform.Tenancy.Catalog.CatalogDbContext</c> in <c>Aurora.Platform.Tenancy</c>. The
/// full-name half can be compiled - a namespace is free to declare - and is, so a registration
/// naming it and a base-type chain reaching it are real compiler output. The assembly half cannot
/// be: this type is in <c>Aurora.Architecture.Tests</c>, so as compiled it is the pair's full name
/// in the wrong assembly - not the pair, therefore a tenant context, and used as one - and
/// <c>Fixtures.CatalogFixture</c> relabels it into <c>Aurora.Platform.Tenancy</c> to produce the
/// exempt pair.
/// </para>
/// <para>
/// It lives outside the <c>Violations</c> namespace because it is neither a violation nor a
/// fixture the rule tests select by default: it enters a population only when a test puts it there.
/// It never collides with B-05's type: this project references no production project, and the
/// production population is read from <c>src/</c> assemblies on disk.
/// </para>
/// </remarks>
internal sealed class CatalogDbContext : DbContext
{
    public CatalogDbContext(DbContextOptions options)
        : base(options)
    {
    }
}
