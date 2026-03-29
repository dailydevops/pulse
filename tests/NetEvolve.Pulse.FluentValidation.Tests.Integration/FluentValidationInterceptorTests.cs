namespace NetEvolve.Pulse.FluentValidation.Tests.Integration;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using global::FluentValidation;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse.Extensibility;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

/// <summary>
/// End-to-end integration tests verifying FluentValidation interceptor behavior
/// through the full mediator pipeline.
/// </summary>
public sealed class FluentValidationInterceptorTests
{
    private static ServiceCollection CreateServiceCollection()
    {
        var services = new ServiceCollection();
        _ = services.AddLogging().AddSingleton(TimeProvider.System);
        return services;
    }

    [Test]
    public async Task ValidRequest_WithRegisteredValidator_PassesThroughToHandler()
    {
        // Arrange
        var services = CreateServiceCollection();
        var handlerCalled = false;

        _ = services
            .AddScoped<IValidator<CreateOrderCommand>, CreateOrderCommandValidator>()
            .AddScoped<ICommandHandler<CreateOrderCommand, OrderResult>>(_ => new CreateOrderCommandHandler(() =>
                handlerCalled = true
            ))
            .AddPulse(configurator => configurator.AddFluentValidation());

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        var result = await mediator
            .SendAsync<CreateOrderCommand, OrderResult>(new CreateOrderCommand("order-1", 10))
            .ConfigureAwait(false);

        // Assert
        using (Assert.Multiple())
        {
            _ = await Assert.That(handlerCalled).IsTrue();
            _ = await Assert.That(result.OrderId).IsEqualTo("order-1");
        }
    }

    [Test]
    public async Task InvalidRequest_WithRegisteredValidator_ThrowsValidationException()
    {
        // Arrange
        var services = CreateServiceCollection();
        var handlerCalled = false;

        _ = services
            .AddScoped<IValidator<CreateOrderCommand>, CreateOrderCommandValidator>()
            .AddScoped<ICommandHandler<CreateOrderCommand, OrderResult>>(_ => new CreateOrderCommandHandler(() =>
                handlerCalled = true
            ))
            .AddPulse(configurator => configurator.AddFluentValidation());

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act & Assert — empty name should fail validation
        _ = await Assert
            .That(() => mediator.SendAsync<CreateOrderCommand, OrderResult>(new CreateOrderCommand(string.Empty, 0))!)
            .Throws<ValidationException>();

        _ = await Assert.That(handlerCalled).IsFalse();
    }

    [Test]
    public async Task Request_WithoutRegisteredValidator_PassesThroughToHandler()
    {
        // Arrange — no IValidator<CreateOrderCommand> registered
        var services = CreateServiceCollection();
        var handlerCalled = false;

        _ = services
            .AddScoped<ICommandHandler<CreateOrderCommand, OrderResult>>(_ => new CreateOrderCommandHandler(() =>
                handlerCalled = true
            ))
            .AddPulse(configurator => configurator.AddFluentValidation());

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act — even with invalid data, no validator means no exception
        var result = await mediator
            .SendAsync<CreateOrderCommand, OrderResult>(new CreateOrderCommand(string.Empty, 0))
            .ConfigureAwait(false);

        // Assert
        using (Assert.Multiple())
        {
            _ = await Assert.That(handlerCalled).IsTrue();
            _ = await Assert.That(result.OrderId).IsEqualTo(string.Empty);
        }
    }

    [Test]
    public async Task InvalidRequest_WithMultipleValidators_AggregatesAllFailures()
    {
        // Arrange
        var services = CreateServiceCollection();

        _ = services
            .AddScoped<IValidator<CreateOrderCommand>, CreateOrderCommandValidator>()
            .AddScoped<IValidator<CreateOrderCommand>, AdditionalCreateOrderCommandValidator>()
            .AddScoped<ICommandHandler<CreateOrderCommand, OrderResult>>(_ => new CreateOrderCommandHandler(() => { }))
            .AddPulse(configurator => configurator.AddFluentValidation());

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act — use an OrderId exceeding 50 chars with negative quantity so both validators fail
        var exception = await Assert
            .That(() =>
                mediator.SendAsync<CreateOrderCommand, OrderResult>(new CreateOrderCommand(new string('X', 51), -1))!
            )
            .Throws<ValidationException>();

        // Assert — failures from both validators should be aggregated
        _ = await Assert.That(exception!.Errors.Count()).IsGreaterThanOrEqualTo(2);
    }

    [Test]
    public async Task ValidQuery_WithRegisteredValidator_PassesThroughToHandler()
    {
        // Arrange
        var services = CreateServiceCollection();

        _ = services
            .AddScoped<IValidator<GetOrderQuery>, GetOrderQueryValidator>()
            .AddScoped<IQueryHandler<GetOrderQuery, OrderResult>>(_ => new GetOrderQueryHandler())
            .AddPulse(configurator => configurator.AddFluentValidation());

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        var result = await mediator
            .QueryAsync<GetOrderQuery, OrderResult>(new GetOrderQuery("order-42"))
            .ConfigureAwait(false);

        // Assert
        _ = await Assert.That(result.OrderId).IsEqualTo("order-42");
    }

    [Test]
    public async Task InvalidQuery_WithRegisteredValidator_ThrowsValidationException()
    {
        // Arrange
        var services = CreateServiceCollection();

        _ = services
            .AddScoped<IValidator<GetOrderQuery>, GetOrderQueryValidator>()
            .AddScoped<IQueryHandler<GetOrderQuery, OrderResult>>(_ => new GetOrderQueryHandler())
            .AddPulse(configurator => configurator.AddFluentValidation());

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act & Assert
        _ = await Assert
            .That(() => mediator.QueryAsync<GetOrderQuery, OrderResult>(new GetOrderQuery(string.Empty))!)
            .Throws<ValidationException>();
    }

    // --- Commands and Queries ---

    private sealed record CreateOrderCommand(string OrderId, int Quantity) : ICommand<OrderResult>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed record GetOrderQuery(string OrderId) : IQuery<OrderResult>
    {
        public string? CorrelationId { get; set; }
    }

    private sealed record OrderResult(string OrderId);

    // --- Validators ---

    private sealed class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
    {
        [SuppressMessage(
            "Major Code Smell",
            "S1144:Unused private types or members should be removed",
            Justification = "Validator is used by FluentValidation interceptor."
        )]
        public CreateOrderCommandValidator()
        {
            _ = RuleFor(x => x.OrderId).NotEmpty().WithMessage("OrderId must not be empty.");
            _ = RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("Quantity must be greater than zero.");
        }
    }

    private sealed class AdditionalCreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
    {
        [SuppressMessage(
            "Major Code Smell",
            "S1144:Unused private types or members should be removed",
            Justification = "Validator is used by FluentValidation interceptor."
        )]
        public AdditionalCreateOrderCommandValidator() =>
            RuleFor(x => x.OrderId)
                .MaximumLength(50)
                .WithMessage("OrderId must not exceed 50 characters (additional validator).");
    }

    private sealed class GetOrderQueryValidator : AbstractValidator<GetOrderQuery>
    {
        [SuppressMessage(
            "Major Code Smell",
            "S1144:Unused private types or members should be removed",
            Justification = "Validator is used by FluentValidation interceptor."
        )]
        public GetOrderQueryValidator() => RuleFor(x => x.OrderId).NotEmpty().WithMessage("OrderId must not be empty.");
    }

    // --- Handlers ---

    private sealed class CreateOrderCommandHandler(Action onHandlerCalled)
        : ICommandHandler<CreateOrderCommand, OrderResult>
    {
        public Task<OrderResult> HandleAsync(CreateOrderCommand command, CancellationToken cancellationToken = default)
        {
            onHandlerCalled();
            return Task.FromResult(new OrderResult(command.OrderId));
        }
    }

    private sealed class GetOrderQueryHandler : IQueryHandler<GetOrderQuery, OrderResult>
    {
        public Task<OrderResult> HandleAsync(GetOrderQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new OrderResult(query.OrderId));
    }
}
