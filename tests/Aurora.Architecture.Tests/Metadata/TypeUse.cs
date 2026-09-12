using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Aurora.Architecture.Tests.Metadata;

/// <summary>
/// One use of a type in metadata: a field's type, a parameter, a return type, a local, a generic
/// argument. <see cref="Display"/> is for humans; <see cref="Names"/> is what rules match on.
/// </summary>
/// <remarks>
/// Rules match on <see cref="Names"/> - the flattened set of every named type this use mentions -
/// rather than on <see cref="Display"/>, because substring matching over a rendered signature is
/// how a rule ends up claiming more than it checks. <c>List&lt;double&gt;</c> contributes both
/// <c>System.Collections.Generic.List`1</c> and <c>System.Double</c> as whole names, so a rule
/// asking "does this mention System.Double" gets an exact answer and a hypothetical
/// <c>Acme.MySystem.Double</c> is not mistaken for one.
/// </remarks>
internal sealed record TypeUse(string Display, ImmutableHashSet<string> Names)
{
    public static TypeUse Named(string fullName) =>
        new(fullName, ImmutableHashSet.Create(StringComparer.Ordinal, fullName));

    public static TypeUse Unnamed(string display) => new(display, ImmutableHashSet<string>.Empty);

    public static TypeUse Combine(string display, IEnumerable<TypeUse> parts) =>
        new(display, parts.Aggregate(
            ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal),
            static (accumulated, part) => accumulated.Union(part.Names)));

    /// <summary>Does this use mention the named type, as a whole name rather than a substring?</summary>
    public bool Mentions(string fullName) => Names.Contains(fullName);

    public bool MentionsAny(IEnumerable<string> fullNames)
    {
        foreach (string fullName in fullNames)
        {
            if (Names.Contains(fullName))
            {
                return true;
            }
        }

        return false;
    }

    public override string ToString() => Display;
}
