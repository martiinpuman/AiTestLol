using System;
using System.Linq.Expressions;
using Aurora.SharedKernel;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Aurora.Platform.Tenancy.Catalog;

/// <summary>
/// The one EF Core value converter every strongly-typed identifier in the system shares:
/// <typeparamref name="TId"/> in the model, <see cref="Guid"/> in the column.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IEntityId{TSelf}"/> carries both halves of the conversion, so one generic converter
/// serves <c>TenantId</c>, <c>SubscriptionId</c> and every identifier a module adds later, rather
/// than a class per id type.
/// </para>
/// <para>
/// The read half goes through a delegate. An expression tree may not name a static abstract
/// interface member, so <c>stored =&gt; TId.From(stored)</c> does not compile; binding
/// <c>TId.From</c> to a delegate first and invoking that inside the expression does, and EF Core
/// compiles the resulting <c>Invoke</c> node for materialisation without complaint. The unit test
/// <c>EntityIdContract</c> in the kernel proves the pair compiles and round-trips; the catalog's
/// integration tests are where EF Core is shown to accept it.
/// </para>
/// <para>
/// Reading is as strict as constructing: an all-zero <see cref="Guid"/> coming out of a column
/// throws, because <see cref="IEntityId{TSelf}.From"/> throws, and a row that identifies nothing
/// should not materialise as if it did.
/// </para>
/// </remarks>
internal sealed class EntityIdConverter<TId> : ValueConverter<TId, Guid>
    where TId : struct, IEntityId<TId>
{
    public EntityIdConverter()
        : base(id => id.Value, FromProvider())
    {
    }

    private static Expression<Func<Guid, TId>> FromProvider()
    {
        Func<Guid, TId> rebuild = TId.From;
        return stored => rebuild(stored);
    }
}
