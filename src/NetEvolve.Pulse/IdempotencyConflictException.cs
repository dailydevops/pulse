namespace NetEvolve.Pulse;

/// <summary>
/// Exception thrown when a command with a previously processed idempotency key is received,
/// indicating a duplicate command that should not be executed again.
/// </summary>
/// <remarks>
/// <para><strong>When Is This Thrown:</strong></para>
/// The <c>IdempotencyCommandInterceptor</c> throws this exception when
/// <see cref="Extensibility.IIdempotencyStore.ExistsAsync"/> returns <see langword="true"/>
/// for the command's idempotency key, meaning the command was already processed successfully.
/// <para><strong>Handling Recommendations:</strong></para>
/// <list type="bullet">
/// <item><description>HTTP APIs: map to <c>409 Conflict</c> or <c>200 OK</c> depending on your idempotency policy.</description></item>
/// <item><description>Message consumers: acknowledge the message without re-processing.</description></item>
/// <item><description>Background workers: skip the command and continue with the next item.</description></item>
/// </list>
/// </remarks>
/// <seealso cref="Extensibility.IIdempotentCommand{TResponse}"/>
/// <seealso cref="Extensibility.IIdempotencyStore"/>
public sealed class IdempotencyConflictException : Exception
{
    /// <summary>
    /// Gets the idempotency key that was already processed.
    /// </summary>
    public string IdempotencyKey { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotencyConflictException"/> class
    /// with a default message.
    /// </summary>
    /// <remarks>This constructor exists to satisfy the standard exception pattern (CA1032). Prefer
    /// <see cref="IdempotencyConflictException(string)"/> to preserve the conflicting key.</remarks>
    public IdempotencyConflictException()
        : base("A command with the given idempotency key has already been processed.") => IdempotencyKey = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotencyConflictException"/> class
    /// with the conflicting idempotency key.
    /// </summary>
    /// <param name="idempotencyKey">The idempotency key that was already processed.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="idempotencyKey"/> is <see langword="null"/>.</exception>
    public IdempotencyConflictException(string idempotencyKey)
        : base($"A command with idempotency key '{idempotencyKey}' has already been processed.")
    {
        ArgumentNullException.ThrowIfNull(idempotencyKey);
        IdempotencyKey = idempotencyKey;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotencyConflictException"/> class
    /// with the conflicting idempotency key, a specified error message, and a reference to the
    /// inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="idempotencyKey">The idempotency key that was already processed.</param>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="idempotencyKey"/> is <see langword="null"/>.</exception>
    public IdempotencyConflictException(string idempotencyKey, string message, Exception innerException)
        : base(message, innerException)
    {
        ArgumentNullException.ThrowIfNull(idempotencyKey);
        IdempotencyKey = idempotencyKey;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotencyConflictException"/> class
    /// with a specified error message and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    /// <remarks>This constructor exists to satisfy the standard exception pattern (CA1032). Prefer
    /// <see cref="IdempotencyConflictException(string, string, Exception)"/> to preserve the conflicting key.</remarks>
    public IdempotencyConflictException(string message, Exception innerException)
        : base(message, innerException) => IdempotencyKey = string.Empty;
}
