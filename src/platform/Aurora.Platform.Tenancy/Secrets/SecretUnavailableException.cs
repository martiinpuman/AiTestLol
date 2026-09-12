using System;
using Aurora.Platform.Tenancy.Catalog;

namespace Aurora.Platform.Tenancy.Secrets;

/// <summary>
/// A secret the catalog references could not be read from the host's store. Names the reference
/// and the reason; never a value.
/// </summary>
internal sealed class SecretUnavailableException : Exception
{
    public SecretUnavailableException(SecretReference reference, string reason)
        : base($"The secret referenced by '{reference}' is unavailable: {reason}.")
    {
        Reference = reference;
    }

    public SecretReference Reference { get; }
}
