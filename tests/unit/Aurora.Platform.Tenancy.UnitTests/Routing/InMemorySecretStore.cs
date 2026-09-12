using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Aurora.Platform.Tenancy.Catalog;
using Aurora.Platform.Tenancy.Secrets;

namespace Aurora.Platform.Tenancy.UnitTests.Routing;

/// <summary>An <see cref="ISecretStore"/> over a dictionary, counting reads so a test can see when the credential is fetched.</summary>
internal sealed class InMemorySecretStore : ISecretStore
{
    private readonly ConcurrentDictionary<string, string> _secrets = new();
    private int _reads;

    public int Reads => Volatile.Read(ref _reads);

    public InMemorySecretStore Holding(string reference, string value)
    {
        _secrets[reference] = value;
        return this;
    }

    public ValueTask<string> ReadAsync(SecretReference reference, CancellationToken ct)
    {
        Interlocked.Increment(ref _reads);
        return _secrets.TryGetValue(reference.Value, out string? value)
            ? ValueTask.FromResult(value)
            : throw new SecretUnavailableException(reference, "not held by this in-memory store");
    }
}
