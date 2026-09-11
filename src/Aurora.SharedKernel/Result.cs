using System;

namespace Aurora.SharedKernel;

/// <summary>
/// The outcome of something that was expected to be able to fail: it worked, or it did not and
/// here is the <see cref="SharedKernel.Error"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this is for.</b> An expected failure is a business outcome — the invoice is already
/// posted, the period is closed, there is no stock. A use case returns those, because a caller has
/// to decide what to do about them, and because using exceptions for outcomes that happen every
/// day turns normal business into stack traces.
/// </para>
/// <para>
/// <b>What this is not for.</b> A broken invariant still throws (ADR-0017 layer 2): reaching an
/// aggregate with data that breaks a rule is a programming error or a missing validator, not a
/// business outcome. Input shape is still FluentValidation's job at the boundary (layer 1). If
/// every method in a call chain returns a <see cref="Result"/>, the model has stopped protecting
/// itself.
/// </para>
/// <para>
/// <b>Deliberately absent</b>: <c>Map</c>, <c>Bind</c>, <c>Then</c>, <c>Match</c> and the rest of
/// the railway-oriented toolkit. Checking <see cref="IsFailure"/> and returning is clearer in a
/// handler than a chain of lambdas, and this type is meant to stay something a new developer reads
/// once rather than a library they have to learn.
/// </para>
/// <para>
/// <b>It fails closed.</b> A <see langword="default"/> <see cref="Result"/> is a failure carrying
/// <see cref="SharedKernel.Error.Unassigned"/>, never a success. A result nobody set must not be
/// able to wave a caller through.
/// </para>
/// </remarks>
public readonly record struct Result
{
    private readonly bool _isSuccess;
    private readonly Error? _error;

    private Result(bool isSuccess, Error? error)
    {
        _isSuccess = isSuccess;
        _error = error;
    }

    /// <summary>Whether it worked.</summary>
    public bool IsSuccess => _isSuccess;

    /// <summary>Whether it did not work.</summary>
    public bool IsFailure => !_isSuccess;

    /// <summary>Why it did not work.</summary>
    /// <exception cref="InvalidOperationException">It worked, so there is no error to read.</exception>
    public Error Error => _isSuccess
        ? throw SucceededSoThereIsNoError()
        : _error ?? SharedKernel.Error.Unassigned;

    /// <summary>It worked.</summary>
    public static Result Success() => new(true, null);

    /// <summary>It did not work, for this reason.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public static Result Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result(false, error);
    }

    /// <summary>It worked, and produced this.</summary>
    /// <remarks>
    /// The factories for <see cref="Result{TValue}"/> live here rather than on it so that the type
    /// argument is inferred from the value — <c>Result.Success(invoice)</c>, not
    /// <c>Result&lt;Invoice&gt;.Success(invoice)</c>.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static Result<TValue> Success<TValue>(TValue value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(
                nameof(value),
                "A successful Result<T> always carries a value. Something that succeeds without " +
                "producing anything is a Result, not a Result<T>.");
        }

        return new Result<TValue>(true, value, null);
    }

    /// <summary>It did not work, for this reason, and so produced nothing.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public static Result<TValue> Failure<TValue>(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<TValue>(false, default, error);
    }

    /// <summary>Reads a failure as the outcome it is, so that a handler can <c>return error;</c>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public static implicit operator Result(Error error) => Failure(error);

    /// <summary>A rendering for logs and test failures.</summary>
    public override string ToString() => _isSuccess ? "Success" : $"Failure({Error})";

    internal static InvalidOperationException SucceededSoThereIsNoError() =>
        new("This Result succeeded, so it carries no error. Check IsFailure before reading Error.");
}

/// <summary>
/// The outcome of something that was expected to be able to fail and produces a value when it does
/// not: the value, or the <see cref="SharedKernel.Error"/>.
/// </summary>
/// <typeparam name="TValue">What a success carries.</typeparam>
/// <remarks>
/// Everything <see cref="Result"/>'s documentation says applies here. A success always carries a
/// value: a successful outcome holding <see langword="null"/> is a <see cref="Result"/> that
/// should not have been generic.
/// </remarks>
public readonly record struct Result<TValue>
{
    private readonly bool _isSuccess;
    private readonly TValue? _value;
    private readonly Error? _error;

    internal Result(bool isSuccess, TValue? value, Error? error)
    {
        _isSuccess = isSuccess;
        _value = value;
        _error = error;
    }

    /// <summary>Whether it worked.</summary>
    public bool IsSuccess => _isSuccess;

    /// <summary>Whether it did not work.</summary>
    public bool IsFailure => !_isSuccess;

    /// <summary>What it produced.</summary>
    /// <exception cref="InvalidOperationException">It did not work, so there is no value to read.</exception>
    public TValue Value => _isSuccess && _value is not null
        ? _value
        : throw new InvalidOperationException(
            "This Result carries no value because it did not succeed. Check IsSuccess before " +
            $"reading Value; the failure was: {Error}.");

    /// <summary>Why it did not work.</summary>
    /// <exception cref="InvalidOperationException">It worked, so there is no error to read.</exception>
    public Error Error => _isSuccess
        ? throw Result.SucceededSoThereIsNoError()
        : _error ?? SharedKernel.Error.Unassigned;

    /// <summary>Reads a failure as the outcome it is, so that a handler can <c>return error;</c>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public static implicit operator Result<TValue>(Error error) => Result.Failure<TValue>(error);

    /// <summary>A rendering for logs and test failures.</summary>
    public override string ToString() => _isSuccess ? $"Success({_value})" : $"Failure({Error})";
}
