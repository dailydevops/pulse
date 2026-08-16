namespace NetEvolve.Pulse.Extensibility.DeadLetter;

using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;

/// <summary>
/// Provides the shared reflection-based logic to replay a command persisted in the dead letter store.
/// </summary>
/// <remarks>
/// <para><strong>Purpose:</strong></para>
/// <see cref="ICommandDeadLetterManagement.ReplayAsync"/> only has a <see cref="Guid"/> identifier at
/// compile time and must resolve the concrete command and response types from persisted string metadata
/// (<see cref="CommandDeadLetterEntry.CommandType"/> and <see cref="CommandDeadLetterEntry.Payload"/>) at
/// runtime. Because every dead letter store implementation (Entity Framework Core, SQL Server, PostgreSQL,
/// SQLite, MySQL, etc.) needs to perform this exact same reflection dance, the logic is implemented once
/// here and shared by all of them.
/// </remarks>
public static class CommandDeadLetterReplayDispatcher
{
    /// <summary>
    /// Caches resolved command types by their assembly-qualified name, mirroring the resolution pattern
    /// used elsewhere in this codebase for persisted type names. Repeated string-based type resolution
    /// via <see cref="Type.GetType(string, bool)"/> parses the name and probes loaded assemblies on every
    /// call, which is unnecessary overhead when the same command type is replayed repeatedly.
    /// </summary>
    private static readonly ConcurrentDictionary<string, Type> CommandTypeCache = new(StringComparer.Ordinal);

    /// <summary>
    /// Resolves the command and response types from the persisted metadata, deserializes the payload,
    /// and dispatches the resulting command instance via <paramref name="mediator"/>.
    /// </summary>
    /// <param name="mediator">The mediator used to dispatch the replayed command.</param>
    /// <param name="payloadSerializer">The serializer used to deserialize the stored payload.</param>
    /// <param name="commandType">The assembly-qualified type name of the command to replay.</param>
    /// <param name="payload">The JSON serialized command payload.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous dispatch operation.</returns>
    /// <exception cref="ArgumentException"><paramref name="commandType"/> or <paramref name="payload"/> is <see langword="null"/>, empty, or consists only of white-space characters.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="commandType"/> cannot be resolved to a runtime <see cref="Type"/>, the resolved type
    /// does not implement <see cref="ICommand{TResponse}"/>, or the payload fails to deserialize.
    /// </exception>
    public static async Task ReplayAsync(
        IMediatorSendOnly mediator,
        IPayloadSerializer payloadSerializer,
        string commandType,
        string payload,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(mediator);
        ArgumentNullException.ThrowIfNull(payloadSerializer);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandType);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);

        var resolvedType = ResolveCommandType(commandType);
        var responseType = ResolveResponseType(resolvedType, commandType);
        var command = DeserializeCommand(payloadSerializer, resolvedType, commandType, payload);

        var sendAsyncMethod = ResolveSendAsyncMethod().MakeGenericMethod(resolvedType, responseType);

        try
        {
            var result = sendAsyncMethod.Invoke(mediator, [command, cancellationToken]);
            await ((Task)result!).ConfigureAwait(false);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        }
    }

    private static Type ResolveCommandType(string commandType)
    {
        if (CommandTypeCache.TryGetValue(commandType, out var cached))
        {
            return cached;
        }

        var resolved =
            Type.GetType(commandType, throwOnError: false)
            ?? throw new InvalidOperationException($"Cannot resolve command type '{commandType}'.");

        return CommandTypeCache.GetOrAdd(commandType, resolved);
    }

    private static Type ResolveResponseType(Type resolvedType, string commandType)
    {
        foreach (var interfaceType in resolvedType.GetInterfaces())
        {
            if (interfaceType.IsGenericType && interfaceType.GetGenericTypeDefinition() == typeof(ICommand<>))
            {
                return interfaceType.GetGenericArguments()[0];
            }
        }

        throw new InvalidOperationException($"'{commandType}' does not implement ICommand<TResponse>.");
    }

    private static object DeserializeCommand(
        IPayloadSerializer payloadSerializer,
        Type resolvedType,
        string commandType,
        string payload
    )
    {
        var deserializeMethod = typeof(IPayloadSerializer)
            .GetMethod(nameof(IPayloadSerializer.Deserialize), [typeof(string)])!
            .MakeGenericMethod(resolvedType);

        var command = deserializeMethod.Invoke(payloadSerializer, [payload]);

        return command
            ?? throw new InvalidOperationException($"Failed to deserialize payload for command type '{commandType}'.");
    }

    private static MethodInfo ResolveSendAsyncMethod()
    {
        foreach (var method in typeof(IMediatorSendOnly).GetMethods())
        {
            if (
                method.Name == nameof(IMediatorSendOnly.SendAsync)
                && method.IsGenericMethodDefinition
                && method.GetGenericArguments().Length == 2
                && method.GetParameters().Length == 2
            )
            {
                return method;
            }
        }

        throw new InvalidOperationException(
            $"Cannot resolve the two-generic-argument overload of '{nameof(IMediatorSendOnly.SendAsync)}'."
        );
    }
}
