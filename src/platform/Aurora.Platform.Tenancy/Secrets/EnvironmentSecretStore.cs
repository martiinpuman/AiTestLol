using System;
using System.Threading;
using System.Threading.Tasks;
using Aurora.Platform.Tenancy.Catalog;

namespace Aurora.Platform.Tenancy.Secrets;

/// <summary>
/// <see cref="ISecretStore"/> over the process environment: a reference <c>env:NAME</c> is the
/// value of the environment variable <c>NAME</c>.
/// </summary>
/// <remarks>
/// ADR-0011 tier 2 and ADR-0025 rule 5 have the orchestrator inject secrets as environment
/// variables or mounted files, and <c>env:AURORA_NZ1_APP_PASSWORD</c> is the shape
/// <see cref="SecretReference"/> documents. This is the bootstrap store; a store over a vault or a
/// mounted file is a second implementation of the same interface, chosen in the composition root.
/// </remarks>
internal sealed class EnvironmentSecretStore : ISecretStore
{
    public const string Scheme = "env:";

    private readonly Func<string, string?> _readVariable;

    /// <summary>Reads the process environment.</summary>
    public EnvironmentSecretStore()
        : this(Environment.GetEnvironmentVariable)
    {
    }

    /// <summary>
    /// Reads through <paramref name="readVariable"/>, which answers <see langword="null"/> for a
    /// variable that is not set and <c>""</c> for one that is set to nothing — the two cases a
    /// diagnostic must keep apart (PR #14 L-3), and which the process environment API cannot
    /// create on demand for a test.
    /// </summary>
    public EnvironmentSecretStore(Func<string, string?> readVariable)
    {
        ArgumentNullException.ThrowIfNull(readVariable);
        _readVariable = readVariable;
    }

    public ValueTask<string> ReadAsync(SecretReference reference, CancellationToken ct)
    {
        if (!reference.IsSpecified)
        {
            throw new ArgumentException("The secret reference is unassigned.", nameof(reference));
        }

        string text = reference.Value;
        if (!text.StartsWith(Scheme, StringComparison.Ordinal) || text.Length == Scheme.Length)
        {
            throw new SecretUnavailableException(reference, $"this store reads '{Scheme}NAME' references only");
        }

        string name = text[Scheme.Length..];
        string? value = _readVariable(name);

        return value switch
        {
            null => throw new SecretUnavailableException(reference, $"environment variable '{name}' is not set"),
            "" => throw new SecretUnavailableException(
                reference, $"environment variable '{name}' is set but empty; an empty credential is refused"),
            _ => ValueTask.FromResult(value),
        };
    }
}
