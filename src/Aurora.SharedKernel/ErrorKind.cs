namespace Aurora.SharedKernel;

/// <summary>
/// What sort of "no" an <see cref="Error"/> is.
/// </summary>
/// <remarks>
/// <para>
/// These are outcome categories in business terms, not transport codes. The kernel does not know
/// what HTTP is; the boundary maps a kind to a status once (ADR-0013 §3), which is what keeps
/// every endpoint from growing its own table of every error code in the system.
/// </para>
/// <para>
/// Deliberately short. A category that cannot be told apart from another by the caller's next
/// action does not earn a place here.
/// </para>
/// </remarks>
public enum ErrorKind
{
    /// <summary>What was referred to does not exist — in this tenant, which is the only place it could.</summary>
    NotFound,

    /// <summary>It exists, but its current state forbids this: already posted, already used, period closed.</summary>
    Conflict,

    /// <summary>The caller may not do this, whether or not it would otherwise succeed.</summary>
    NotPermitted,

    /// <summary>Well-formed, allowed, and still refused by a business rule: no stock, over the credit limit.</summary>
    Rejected,
}
