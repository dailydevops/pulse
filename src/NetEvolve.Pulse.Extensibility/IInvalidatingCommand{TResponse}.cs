namespace NetEvolve.Pulse.Extensibility;

using System;
using System.Collections.Generic;

/// <summary>
/// Represents a command that, upon successful execution, invalidates the cached results of one or more
/// query types.
/// </summary>
/// <typeparam name="TResponse">The type of response returned after executing the command.</typeparam>
/// <remarks>
/// <para><strong>Purpose:</strong></para>
/// Commands implementing this interface declare which query types' cached results become stale when the
/// command succeeds. This allows a cache invalidation interceptor to evict the corresponding cache entries
/// after the command handler completes.
/// <para><strong>Declared Types:</strong></para>
/// Each <see cref="Type"/> listed in <see cref="InvalidatedQueryTypes"/> is expected to implement
/// <see cref="IQuery{TResponse}"/> for the same <typeparamref name="TResponse"/> as this command. This
/// interface does not enforce that relationship; the runtime check is performed by the consuming
/// interceptor when it processes the invalidation.
/// </remarks>
/// <example>
/// <code>
/// public record UpdateCustomerCommand(string CustomerId, string Name)
///     : IInvalidatingCommand&lt;CustomerUpdatedResult&gt;
/// {
///     public IEnumerable&lt;Type&gt; InvalidatedQueryTypes { get; } = [typeof(GetCustomerByIdQuery)];
/// }
///
/// public record CustomerUpdatedResult(string CustomerId, DateTime UpdatedAt);
/// </code>
/// </example>
/// <seealso cref="ICommand{TResponse}"/>
public interface IInvalidatingCommand<TResponse> : ICommand<TResponse>
{
    /// <summary>
    /// Gets the query types whose cached results should be invalidated after this command succeeds.
    /// </summary>
    /// <remarks>
    /// Each listed type is expected to implement <see cref="IQuery{TResponse}"/> for the same
    /// <typeparamref name="TResponse"/>, though this is not enforced by this interface.
    /// </remarks>
    IEnumerable<Type> InvalidatedQueryTypes { get; }
}
