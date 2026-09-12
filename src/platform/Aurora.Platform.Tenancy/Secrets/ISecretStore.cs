using System.Threading;
using System.Threading.Tasks;
using Aurora.Platform.Tenancy.Catalog;

namespace Aurora.Platform.Tenancy.Secrets;

/// <summary>
/// The host's secret store (ADR-0011 tier 2), reached through the references the catalog holds:
/// the one way a credential enters a connection string.
/// </summary>
/// <remarks>
/// The catalog stores where a secret lives, never what it is (ADR-0007 §9.2,
/// <see cref="SecretReference"/>). This is the seam that turns the one into the other, at request
/// time, in memory. Nothing else in the module reads a secret's value.
/// </remarks>
internal interface ISecretStore
{
    /// <summary>The secret <paramref name="reference"/> points at.</summary>
    /// <exception cref="SecretUnavailableException">
    /// The reference is not one this store understands, or names a secret that is not present.
    /// The message names the reference and never the value.
    /// </exception>
    ValueTask<string> ReadAsync(SecretReference reference, CancellationToken ct);
}
