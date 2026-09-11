using System;
using System.IO;
using System.Security.Cryptography;

namespace Aurora.Countries.Hosting;

/// <summary>
/// What a Country Package signature covers, and how it is computed (ADR-0008 §9.3).
/// </summary>
/// <remarks>
/// <para>
/// ECDSA on P-256 with SHA-256, because it is entirely in the base class library — no third-party
/// dependency and no licence question. Authenticode is Windows-specific and these are Linux
/// containers; strong naming is not a security feature and is deliberately not used for this.
/// </para>
/// <para>
/// The signed bytes are the SHA-256 of the assembly file followed by the manifest's exact bytes.
/// Hashing the assembly rather than signing it whole keeps the signed payload a fixed 32 bytes
/// regardless of package size; appending the manifest binds the two together, so a signed assembly
/// cannot be paired with somebody else's manifest — which is what would let a package claim a
/// different id, key or schema than the one that was reviewed.
/// </para>
/// </remarks>
public static class PackageSignature
{
    /// <summary>The detached signature file, beside the assembly in the package directory.</summary>
    public const string FileName = "package.sig";

    /// <summary>
    /// The only curve accepted, by OID: P-256 (also called secp256r1 and prime256v1). The OID is
    /// what is enforced — a key size alone does not name a curve, and secp256k1 is 256-bit too.
    /// </summary>
    public const string CurveOid = "1.2.840.10045.3.1.7";

    /// <summary>The size of a key on that curve, in bits — for messages, not for the check.</summary>
    public const int KeySizeInBits = 256;

    /// <summary>The hash the signature is over.</summary>
    public static HashAlgorithmName HashAlgorithm => HashAlgorithmName.SHA256;

    /// <summary>
    /// The exact bytes a signature is computed over: SHA-256 of the assembly file, then the manifest
    /// bytes.
    /// </summary>
    /// <param name="assemblyPath">The package assembly.</param>
    /// <param name="manifestBytes">The manifest exactly as it is embedded.</param>
    public static byte[] ContentToSign(string assemblyPath, ReadOnlySpan<byte> manifestBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);

        using FileStream assembly = File.OpenRead(assemblyPath);
        byte[] assemblyHash = SHA256.HashData(assembly);

        byte[] content = new byte[assemblyHash.Length + manifestBytes.Length];
        assemblyHash.CopyTo(content.AsSpan());
        manifestBytes.CopyTo(content.AsSpan(assemblyHash.Length));
        return content;
    }
}
