using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aurora.Platform.Tenancy.Routing;

/// <summary>
/// A connection string that carries a credential and renders as <c>&lt;redacted&gt;</c> on every
/// path but <see cref="Reveal"/>: <c>ToString()</c>, string interpolation, a record's generated
/// printing, <c>System.Text.Json</c>, and every reflection-based sink — the value has no public
/// property for one to read (PR #14 M-2, ADR-0016).
/// </summary>
/// <remarks>
/// Redaction is a property of the value, not of one rendering of one container, so it survives
/// the value being placed inside another record, a log event or a serialised payload.
/// Deserialising one is refused: a credential never re-enters the process from a serialised form.
/// </remarks>
[JsonConverter(typeof(RedactingConverter))]
internal sealed class ConnectionSecret : IEquatable<ConnectionSecret>
{
    /// <summary>What every rendering but <see cref="Reveal"/> shows.</summary>
    public const string Redacted = "<redacted>";

    private readonly string _connectionString;

    /// <exception cref="ArgumentException"><paramref name="connectionString"/> is blank.</exception>
    public ConnectionSecret(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    /// <summary>
    /// The connection string, credential included. Hand it to an <c>NpgsqlDataSourceBuilder</c>;
    /// never to a log.
    /// </summary>
    public string Reveal() => _connectionString;

    public override string ToString() => Redacted;

    public bool Equals(ConnectionSecret? other) =>
        other is not null && string.Equals(_connectionString, other._connectionString, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as ConnectionSecret);

    public override int GetHashCode() => _connectionString.GetHashCode(StringComparison.Ordinal);

    /// <summary>Writes <see cref="Redacted"/>; refuses to read.</summary>
    internal sealed class RedactingConverter : JsonConverter<ConnectionSecret>
    {
        public override ConnectionSecret Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException("A connection secret is never rehydrated from a serialised form.");

        public override void Write(Utf8JsonWriter writer, ConnectionSecret value, JsonSerializerOptions options) =>
            writer.WriteStringValue(Redacted);
    }
}
