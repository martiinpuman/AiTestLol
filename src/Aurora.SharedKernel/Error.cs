using System;
using System.Diagnostics.CodeAnalysis;

namespace Aurora.SharedKernel;

/// <summary>
/// One expected failure: what kind of "no" it is, a stable code, and a sentence for whoever reads
/// the log.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Code"/> is the identity of the failure and never changes once shipped. It is what
/// the boundary localizes (ADR-0017: user-facing messages are resource keys, never English prose
/// in code) and what an API client matches on. Dotted, lower case, module first —
/// <c>sales.invoice.already_posted</c> — so that a code sorts next to its neighbours and reads as
/// a path to the rule that produced it.
/// </para>
/// <para>
/// <see cref="Description"/> is for developers: it goes in logs and test output and is never shown
/// to a user, which is why plain English is right here and wrong in a validator message. Say what
/// was refused and, where it helps, which value refused it.
/// </para>
/// </remarks>
[SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification =
        "`Error` is the name docs/architecture/modules.md §3 gives this type, and the ubiquitous " +
        "language is worth more than the rule protects. CA1716 guards consumers writing Visual " +
        "Basic, where `Error` is a keyword; this assembly is consumed only by the C# projects in " +
        "this solution and is never published to a feed.")]
public sealed record Error
{
    /// <summary>
    /// The failure a <see langword="default"/> <see cref="Result"/> reports, so that an
    /// unassigned result fails closed and says why instead of passing for a success.
    /// </summary>
    public static Error Unassigned { get; } = new(
        ErrorKind.Rejected,
        "kernel.result.unassigned",
        "This Result was never assigned a success or a failure. It came from `default(Result)` or " +
        "an unassigned field, and a result nobody set is not a success.");

    private Error(ErrorKind kind, string code, string description)
    {
        Kind = kind;
        Code = Required(code, nameof(code));
        Description = Required(description, nameof(description));
    }

    /// <summary>What sort of "no" this is.</summary>
    public ErrorKind Kind { get; }

    /// <summary>The stable, machine-readable identity of the failure, and its localization key.</summary>
    public string Code { get; }

    /// <summary>A developer-facing sentence for logs and test output. Never shown to a user.</summary>
    public string Description { get; }

    /// <summary>What was referred to does not exist.</summary>
    public static Error NotFound(string code, string description) =>
        new(ErrorKind.NotFound, code, description);

    /// <summary>It exists, but its current state forbids this.</summary>
    public static Error Conflict(string code, string description) =>
        new(ErrorKind.Conflict, code, description);

    /// <summary>The caller may not do this.</summary>
    public static Error NotPermitted(string code, string description) =>
        new(ErrorKind.NotPermitted, code, description);

    /// <summary>Well-formed and allowed, and still refused by a business rule.</summary>
    public static Error Rejected(string code, string description) =>
        new(ErrorKind.Rejected, code, description);

    /// <summary>A rendering for logs and test failures, such as <c>Conflict sales.invoice.already_posted: …</c>.</summary>
    public override string ToString() => $"{Kind} {Code}: {Description}";

    private static string Required(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);

        if (value.Length == 0)
        {
            throw new ArgumentException(
                "An error must carry both a code and a description: the code is what the boundary " +
                "localizes and a client matches on, and the description is what makes the log " +
                "worth reading.",
                parameterName);
        }

        return value;
    }
}
