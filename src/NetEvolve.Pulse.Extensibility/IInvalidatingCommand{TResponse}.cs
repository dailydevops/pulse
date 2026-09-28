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
/// Each <see cref="Type"/> listed in <see cref="InvalidatedQueryTypes"/> is expected to be a query type,
/// typically one implementing <c>ICacheableQuery&lt;T&gt;</c>. Its response type does not need to match
/// <typeparamref name="TResponse"/>. The relationship is not validated: a type for which no cache keys were
/// recorded is ignored.
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
    /// Each listed type is expected to be a query type. Types for which no cache keys were recorded are
    /// ignored.
    /// </remarks>
    IEnumerable<Type> InvalidatedQueryTypes { get; }
}
