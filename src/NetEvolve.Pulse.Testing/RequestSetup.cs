namespace NetEvolve.Pulse.Testing;

/// <summary>
/// Fluent builder for configuring canned responses or exceptions for command and query setups
/// in <see cref="FakeMediator"/>.
/// </summary>
/// <typeparam name="TResponse">The type of response to return when the configured request is invoked.</typeparam>
public sealed class RequestSetup<TResponse>
{
    private readonly FakeMediator _mediator;
    private readonly Type _requestType;

    internal RequestSetup(FakeMediator mediator, Type requestType)
    {
        _mediator = mediator;
        _requestType = requestType;
    }

    /// <summary>
    /// Configures the setup to return the specified response value when the request is invoked.
    /// </summary>
    /// <param name="value">The canned response value to return.</param>
    /// <returns>The <see cref="FakeMediator"/> instance for fluent chaining.</returns>
    public FakeMediator Returns(TResponse value)
    {
        _mediator.RegisterResponse(_requestType, value);
        return _mediator;
    }

    /// <summary>
    /// Configures the setup to throw the specified exception when the request is invoked.
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
    /// (created via parameterless constructor) when the request is invoked.
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
