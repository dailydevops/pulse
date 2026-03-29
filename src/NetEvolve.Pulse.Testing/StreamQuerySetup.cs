namespace NetEvolve.Pulse.Testing;

using System.Collections.Generic;

/// <summary>
/// Fluent builder for configuring canned streaming responses or exceptions for streaming query setups
/// in <see cref="FakeMediator"/>.
/// </summary>
/// <typeparam name="TResponse">The type of each item yielded when the configured streaming query is invoked.</typeparam>
public sealed class StreamQuerySetup<TResponse>
{
    private readonly FakeMediator _mediator;
    private readonly Type _requestType;

    internal StreamQuerySetup(FakeMediator mediator, Type requestType)
    {
        _mediator = mediator;
        _requestType = requestType;
    }

    /// <summary>
    /// Configures the setup to yield the specified items when the streaming query is invoked.
    /// </summary>
    /// <param name="items">The canned items to yield.</param>
    /// <returns>The <see cref="FakeMediator"/> instance for fluent chaining.</returns>
    public FakeMediator Returns(IEnumerable<TResponse> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        _mediator.RegisterStreamResponse(_requestType, items.Cast<object>());
        return _mediator;
    }

    /// <summary>
    /// Configures the setup to yield the specified items when the streaming query is invoked.
    /// </summary>
    /// <param name="items">The canned items to yield.</param>
    /// <returns>The <see cref="FakeMediator"/> instance for fluent chaining.</returns>
    public FakeMediator Returns(params TResponse[] items)
    {
        ArgumentNullException.ThrowIfNull(items);

        _mediator.RegisterStreamResponse(_requestType, items.Cast<object>());
        return _mediator;
    }

    /// <summary>
    /// Configures the setup to throw the specified exception when the streaming query is invoked.
    /// </summary>
    /// <param name="exception">The exception to throw.</param>
    /// <returns>The <see cref="FakeMediator"/> instance for fluent chaining.</returns>
    public FakeMediator Throws(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        _mediator.RegisterException(_requestType, exception);
        return _mediator;
    }

    /// <summary>
    /// Configures the setup to throw an exception of type <typeparamref name="TException"/>
    /// (created via parameterless constructor) when the streaming query is invoked.
    /// </summary>
    /// <typeparam name="TException">The type of exception to throw. Must have a parameterless constructor.</typeparam>
    /// <returns>The <see cref="FakeMediator"/> instance for fluent chaining.</returns>
    public FakeMediator Throws<TException>()
        where TException : Exception, new()
    {
        _mediator.RegisterException(_requestType, new TException());
        return _mediator;
    }
}
