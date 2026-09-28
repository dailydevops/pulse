namespace NetEvolve.Pulse.SourceGeneration.Tests.Unit;

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetEvolve.Extensions.TUnit;
using NetEvolve.Pulse.SourceGeneration.Generators;
using TUnit.Core;

/// <summary>
/// Verifies that <c>[PulseHandler&lt;T&gt;]</c> on a generic handler whose type parameter constraints are not
/// satisfied by the message type reports PULSE006 instead of emitting a registration that does not compile.
/// </summary>
[TestGroup("SourceGeneration")]
[TestGroup("SourceGeneration.PulseHandler")]
public class PulseHandlerGeneratorConstraintTests
{
    private const string Preamble = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using NetEvolve.Pulse.Extensibility;
        using NetEvolve.Pulse.Extensibility.Attributes;

        public interface IMarker { }

        public abstract record RequestBase
        {
            public string? CausationId { get; set; }
            public string? CorrelationId { get; set; }
        }

        """;

    [Test]
    [Arguments(
        "FixedResultTypeMismatch",
        """
            public sealed record C1 : RequestBase, ICommand<string>;

            [PulseHandler<C1>]
            public sealed class G1<TCmd> : ICommandHandler<TCmd, int>
                where TCmd : ICommand<int>
            {
                public Task<int> HandleAsync(TCmd command, CancellationToken cancellationToken = default) => Task.FromResult(0);
            }
            """
    )]
    [Arguments(
        "ExtraInterfaceConstraint",
        """
            public sealed record C1 : RequestBase, ICommand<string>;

            [PulseHandler<C1>]
            public sealed class G1<TCmd, TResult> : ICommandHandler<TCmd, TResult>
                where TCmd : ICommand<TResult>, IMarker
            {
                public Task<TResult> HandleAsync(TCmd command, CancellationToken cancellationToken = default) => Task.FromResult(default(TResult)!);
            }
            """
    )]
    [Arguments(
        "ClassConstraintOnValueTypeMessage",
        """
            public record struct C1 : ICommand<string>
            {
                public string? CausationId { get; set; }
                public string? CorrelationId { get; set; }
            }

            [PulseHandler<C1>]
            public sealed class G1<TCmd, TResult> : ICommandHandler<TCmd, TResult>
                where TCmd : class, ICommand<TResult>
            {
                public Task<TResult> HandleAsync(TCmd command, CancellationToken cancellationToken = default) => Task.FromResult(default(TResult)!);
            }
            """
    )]
    [Arguments(
        "StructConstraintOnReferenceTypeMessage",
        """
            public sealed record C1 : RequestBase, ICommand<string>;

            [PulseHandler<C1>]
            public sealed class G1<TCmd, TResult> : ICommandHandler<TCmd, TResult>
                where TCmd : struct, ICommand<TResult>
            {
                public Task<TResult> HandleAsync(TCmd command, CancellationToken cancellationToken = default) => Task.FromResult(default(TResult)!);
            }
            """
    )]
    [Arguments(
        "NewConstraintWithoutParameterlessConstructor",
        """
            public sealed record C1(string Name) : RequestBase, ICommand<string>;

            [PulseHandler<C1>]
            public sealed class G1<TCmd, TResult> : ICommandHandler<TCmd, TResult>
                where TCmd : ICommand<TResult>, new()
            {
                public Task<TResult> HandleAsync(TCmd command, CancellationToken cancellationToken = default) => Task.FromResult(default(TResult)!);
            }
            """
    )]
    [Arguments(
        "NewConstraintOnAbstractMessage",
        """
            public abstract record C1 : RequestBase, ICommand<string>;

            [PulseHandler<C1>]
            public sealed class G1<TCmd, TResult> : ICommandHandler<TCmd, TResult>
                where TCmd : ICommand<TResult>, new()
            {
                public Task<TResult> HandleAsync(TCmd command, CancellationToken cancellationToken = default) => Task.FromResult(default(TResult)!);
            }
            """
    )]
    [Arguments(
        "BaseTypeConstraint",
        """
            public abstract record CommandBase : RequestBase, ICommand<string>;
            public sealed record C1 : RequestBase, ICommand<string>;

            [PulseHandler<C1>]
            public sealed class G1<TCmd, TResult> : ICommandHandler<TCmd, TResult>
                where TCmd : CommandBase, ICommand<TResult>
            {
                public Task<TResult> HandleAsync(TCmd command, CancellationToken cancellationToken = default) => Task.FromResult(default(TResult)!);
            }
            """
    )]
    [Arguments(
        "NestedGenericConstraint",
        """
            public sealed record C1 : RequestBase, ICommand<string>;

            [PulseHandler<C1>]
            public sealed class G1<TCmd, TResult> : ICommandHandler<TCmd, TResult>
                where TCmd : ICommand<TResult>, IComparable<TCmd>
            {
                public Task<TResult> HandleAsync(TCmd command, CancellationToken cancellationToken = default) => Task.FromResult(default(TResult)!);
            }
            """
    )]
    [Arguments(
        "StructConstraintOnResultTypeParameter",
        """
            public sealed record Q1 : RequestBase, IQuery<string>;

            [PulseHandler<Q1>]
            public sealed class G1<TQuery, TResult> : IQueryHandler<TQuery, TResult>
                where TQuery : IQuery<TResult>
                where TResult : struct
            {
                public Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken = default) => Task.FromResult(default(TResult));
            }
            """
    )]
    [Arguments(
        "UnmanagedConstraintOnResultTypeParameter",
        """
            public sealed record Q1 : RequestBase, IQuery<string>;

            [PulseHandler<Q1>]
            public sealed class G1<TQuery, TResult> : IQueryHandler<TQuery, TResult>
                where TQuery : IQuery<TResult>
                where TResult : unmanaged
            {
                public Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken = default) => Task.FromResult(default(TResult));
            }
            """
    )]
    [Arguments(
        "ArrayConstraint",
        """
            public sealed record C1 : RequestBase, ICommand<string>;

            [PulseHandler<C1>]
            public sealed class G1<TCmd, TResult> : ICommandHandler<TCmd, TResult>
                where TCmd : ICommand<TResult>, IComparable<TResult[]>
            {
                public Task<TResult> HandleAsync(TCmd command, CancellationToken cancellationToken = default) => Task.FromResult(default(TResult)!);
            }
            """
    )]
    [Arguments(
        "StructConstraintOnNullableResultTypeParameter",
        """
            public sealed record C1 : RequestBase, ICommand<int?>;

            [PulseHandler<C1>]
            public sealed class G1<TCmd, TResult> : ICommandHandler<TCmd, TResult>
                where TCmd : ICommand<TResult>
                where TResult : struct
            {
                public Task<TResult> HandleAsync(TCmd command, CancellationToken cancellationToken = default) => Task.FromResult(default(TResult));
            }
            """
    )]
    [Arguments(
        "ClassConstraintOnStreamQueryResult",
        """
            public sealed record S1 : RequestBase, IStreamQuery<int>;

            [PulseHandler<S1>]
            public sealed class G1<TQuery, TResult> : IStreamQueryHandler<TQuery, TResult>
                where TQuery : IStreamQuery<TResult>
                where TResult : class
            {
                public IAsyncEnumerable<TResult> HandleAsync(TQuery request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            }
            """
    )]
    [Arguments(
        "ExtraInterfaceConstraintOnEventHandler",
        """
            public sealed record E1 : IEvent
            {
                public string Id { get; init; } = Guid.NewGuid().ToString();
                public string? CausationId { get; set; }
                public string? CorrelationId { get; set; }
                public DateTimeOffset? PublishedAt { get; set; }
            }

            [PulseHandler<E1>]
            public sealed class G1<TEvent> : IEventHandler<TEvent>
                where TEvent : IEvent, IMarker
            {
                public Task HandleAsync(TEvent message, CancellationToken cancellationToken = default) => Task.CompletedTask;
            }
            """
    )]
    [Arguments(
        "InterfaceNestedInGenericClassConstraint",
        """
            public class Outer<T>
            {
                public interface IInner { }
            }

            public sealed record C1 : RequestBase, ICommand<string>;

            [PulseHandler<C1>]
            public sealed class G1<TCmd, TResult> : ICommandHandler<TCmd, TResult>
                where TCmd : ICommand<TResult>, Outer<int>.IInner
            {
                public Task<TResult> HandleAsync(TCmd command, CancellationToken cancellationToken = default) => Task.FromResult(default(TResult)!);
            }
            """
    )]
    public async Task WhenGenericHandlerConstraintNotSatisfiedThenPulse006ReportedAndNoGeneratedCodeErrors(
        string scenario,
        string declarations
    )
    {
        var result = RunGenerator(declarations);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result.InputErrors).IsEmpty().Because(scenario);
            _ = await Assert
                .That(result.PulseDiagnostics.Select(d => d.Id))
                .IsEquivalentTo(["PULSE006"])
                .Because(scenario);
            _ = await Assert.That(result.GeneratedErrors).IsEmpty().Because(scenario);
        }
    }

    [Test]
    [Arguments(
        "NestedGenericConstraintSatisfied",
        """
            public sealed record C1 : RequestBase, ICommand<string>;

            [PulseHandler<C1>]
            public sealed class G1<TCmd, TResult> : ICommandHandler<TCmd, TResult>
                where TCmd : class, ICommand<TResult>, IEquatable<TCmd>, new()
                where TResult : notnull
            {
                public Task<TResult> HandleAsync(TCmd command, CancellationToken cancellationToken = default) => Task.FromResult(default(TResult)!);
            }
            """,
        "global::G1<global::C1, string>"
    )]
    [Arguments(
        "BaseTypeAndValueTypeConstraintsSatisfied",
        """
            public abstract record CommandBase : RequestBase, ICommand<int>;
            public sealed record C1 : CommandBase, IMarker;

            [PulseHandler<C1>]
            public sealed class G1<TCmd, TResult> : ICommandHandler<TCmd, TResult>
                where TCmd : CommandBase, ICommand<TResult>, IMarker
                where TResult : unmanaged
            {
                public Task<TResult> HandleAsync(TCmd command, CancellationToken cancellationToken = default) => Task.FromResult(default(TResult));
            }
            """,
        "global::G1<global::C1, int>"
    )]
    [Arguments(
        "ValueTypeMessageWithNewConstraintSatisfied",
        """
            public record struct C1 : ICommand<string>
            {
                public string? CausationId { get; set; }
                public string? CorrelationId { get; set; }
            }

            [PulseHandler<C1>]
            public sealed class G1<TCmd, TResult> : ICommandHandler<TCmd, TResult>
                where TCmd : ICommand<TResult>, new()
            {
                public Task<TResult> HandleAsync(TCmd command, CancellationToken cancellationToken = default) => Task.FromResult(default(TResult)!);
            }
            """,
        "global::G1<global::C1, string>"
    )]
    [Arguments(
        "ArrayConstraintSatisfied",
        """
            public sealed record C1 : RequestBase, ICommand<string>, IComparable<string[]>
            {
                public int CompareTo(string[]? other) => 0;
            }

            [PulseHandler<C1>]
            public sealed class G1<TCmd, TResult> : ICommandHandler<TCmd, TResult>
                where TCmd : ICommand<TResult>, IComparable<TResult[]>
            {
                public Task<TResult> HandleAsync(TCmd command, CancellationToken cancellationToken = default) => Task.FromResult(default(TResult)!);
            }
            """,
        "global::G1<global::C1, string>"
    )]
    [Arguments(
        "EventHandlerConstraintsSatisfied",
        """
            public record struct E1 : IEvent, IMarker
            {
                public E1() { }
                public string Id { get; init; } = Guid.NewGuid().ToString();
                public string? CausationId { get; set; }
                public string? CorrelationId { get; set; }
                public DateTimeOffset? PublishedAt { get; set; }
            }

            [PulseHandler<E1>]
            public sealed class G1<TEvent> : IEventHandler<TEvent>
                where TEvent : struct, IEvent, IMarker
            {
                public Task HandleAsync(TEvent message, CancellationToken cancellationToken = default) => Task.CompletedTask;
            }
            """,
        "global::G1<global::E1>"
    )]
    [Arguments(
        "NotNullConstraintOnNullableValueTypeResult",
        """
            public sealed record C1 : RequestBase, ICommand<int?>;

            [PulseHandler<C1>]
            public sealed class G1<TCmd, TResult> : ICommandHandler<TCmd, TResult>
                where TCmd : ICommand<TResult>
                where TResult : notnull
            {
                public Task<TResult> HandleAsync(TCmd command, CancellationToken cancellationToken = default) => Task.FromResult(default(TResult)!);
            }
            """,
        "global::G1<global::C1, int?>"
    )]
    [Arguments(
        "NotNullConstraintOnNullableReferenceTypeResult",
        """
            public sealed record C1 : RequestBase, ICommand<string?>;

            [PulseHandler<C1>]
            public sealed class G1<TCmd, TResult> : ICommandHandler<TCmd, TResult>
                where TCmd : ICommand<TResult>
                where TResult : notnull
            {
                public Task<TResult> HandleAsync(TCmd command, CancellationToken cancellationToken = default) => Task.FromResult(default(TResult)!);
            }
            """,
        "global::G1<global::C1, string"
    )]
    [Arguments(
        "InterfaceNestedInGenericClassConstraintSatisfied",
        """
            public class Outer<T>
            {
                public interface IInner { }
            }

            public sealed record C1 : RequestBase, ICommand<string>, Outer<int>.IInner;

            [PulseHandler<C1>]
            public sealed class G1<TCmd, TResult> : ICommandHandler<TCmd, TResult>
                where TCmd : ICommand<TResult>, Outer<int>.IInner
            {
                public Task<TResult> HandleAsync(TCmd command, CancellationToken cancellationToken = default) => Task.FromResult(default(TResult)!);
            }
            """,
        "global::G1<global::C1, string>"
    )]
    [Arguments(
        "TypeInGenericClassWithTypeParameterConstraintSatisfied",
        """
            public class Outer<T>
            {
                public interface IInner<U> { }
            }

            public sealed record C1 : RequestBase, ICommand<string>, Outer<string>.IInner<C1>;

            [PulseHandler<C1>]
            public sealed class G1<TCmd, TResult> : ICommandHandler<TCmd, TResult>
                where TCmd : ICommand<TResult>, Outer<TResult>.IInner<TCmd>
            {
                public Task<TResult> HandleAsync(TCmd command, CancellationToken cancellationToken = default) => Task.FromResult(default(TResult)!);
            }
            """,
        "global::G1<global::C1, string>"
    )]
    public async Task WhenGenericHandlerConstraintsSatisfiedThenRegistrationGeneratedWithoutErrors(
        string scenario,
        string declarations,
        string expectedRegistration
    )
    {
        var result = RunGenerator(declarations);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result.InputErrors).IsEmpty().Because(scenario);
            _ = await Assert.That(result.PulseDiagnostics).IsEmpty().Because(scenario);
            _ = await Assert.That(result.GeneratedErrors).IsEmpty().Because(scenario);
            _ = await Assert.That(result.GeneratedSource).Contains(expectedRegistration).Because(scenario);
        }
    }

    [Test]
    [Arguments(
        "ConcreteHandlerWithSeveralEventHandlerInterfaces",
        """
            public sealed record E1 : IEvent
            {
                public string Id { get; init; } = Guid.NewGuid().ToString();
                public string? CausationId { get; set; }
                public string? CorrelationId { get; set; }
                public DateTimeOffset? PublishedAt { get; set; }
            }

            public sealed record E2 : IEvent
            {
                public string Id { get; init; } = Guid.NewGuid().ToString();
                public string? CausationId { get; set; }
                public string? CorrelationId { get; set; }
                public DateTimeOffset? PublishedAt { get; set; }
            }

            [PulseHandler<E2>]
            public sealed class H1 : IEventHandler<E1>, IEventHandler<E2>
            {
                public Task HandleAsync(E1 message, CancellationToken cancellationToken = default) => Task.CompletedTask;
                public Task HandleAsync(E2 message, CancellationToken cancellationToken = default) => Task.CompletedTask;
            }
            """,
        "IEventHandler<global::E2>, global::H1>"
    )]
    [Arguments(
        "ConcreteHandlerWithSeveralCommandHandlerInterfaces",
        """
            public sealed record C1 : RequestBase, ICommand<string>;
            public sealed record C2 : RequestBase, ICommand<int>;

            [PulseHandler<C2>]
            public sealed class H1 : ICommandHandler<C1, string>, ICommandHandler<C2, int>
            {
                public Task<string> HandleAsync(C1 command, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
                public Task<int> HandleAsync(C2 command, CancellationToken cancellationToken = default) => Task.FromResult(0);
            }
            """,
        "ICommandHandler<global::C2, int>, global::H1>"
    )]
    public async Task WhenConcreteHandlerImplementsSeveralHandlerInterfacesThenExplicitMessageTypeIsRegistered(
        string scenario,
        string declarations,
        string expectedRegistration
    )
    {
        var result = RunGenerator(declarations);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result.InputErrors).IsEmpty().Because(scenario);
            _ = await Assert.That(result.PulseDiagnostics).IsEmpty().Because(scenario);
            _ = await Assert.That(result.GeneratedErrors).IsEmpty().Because(scenario);
            _ = await Assert.That(result.GeneratedSource).Contains(expectedRegistration).Because(scenario);
        }
    }

    [Test]
    [Arguments(
        "ConcreteHandlerWithTwoCommandsOfSameResultType",
        """
            public sealed record CmdA : RequestBase, ICommand<string>;
            public sealed record CmdB : RequestBase, ICommand<string>;

            [PulseHandler<CmdA>]
            [PulseHandler<CmdB>]
            public sealed class Multi : ICommandHandler<CmdA, string>, ICommandHandler<CmdB, string>
            {
                public Task<string> HandleAsync(CmdA command, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
                public Task<string> HandleAsync(CmdB command, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
            }
            """,
        "ICommandHandler<global::CmdA, string>>(",
        "ICommandHandler<global::CmdB, string>>("
    )]
    [Arguments(
        "ConcreteHandlerWithTwoQueries",
        """
            public sealed record QryA : RequestBase, IQuery<string>;
            public sealed record QryB : RequestBase, IQuery<int>;

            [PulseHandler<QryA>]
            [PulseHandler<QryB>]
            public sealed class Multi : IQueryHandler<QryA, string>, IQueryHandler<QryB, int>
            {
                public Task<string> HandleAsync(QryA query, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
                public Task<int> HandleAsync(QryB query, CancellationToken cancellationToken = default) => Task.FromResult(0);
            }
            """,
        "IQueryHandler<global::QryA, string>>(",
        "IQueryHandler<global::QryB, int>>("
    )]
    [Arguments(
        "ConcreteHandlerWithTwoStreamQueries",
        """
            public sealed record SqA : RequestBase, IStreamQuery<string>;
            public sealed record SqB : RequestBase, IStreamQuery<string>;

            [PulseHandler<SqA>]
            [PulseHandler<SqB>]
            public sealed class Multi : IStreamQueryHandler<SqA, string>, IStreamQueryHandler<SqB, string>
            {
                public IAsyncEnumerable<string> HandleAsync(SqA request, CancellationToken cancellationToken = default) => Empty();
                public IAsyncEnumerable<string> HandleAsync(SqB request, CancellationToken cancellationToken = default) => Empty();

                private static async IAsyncEnumerable<string> Empty()
                {
                    await Task.CompletedTask;
                    yield break;
                }
            }
            """,
        "IStreamQueryHandler<global::SqA, string>>(",
        "IStreamQueryHandler<global::SqB, string>>("
    )]
    [Arguments(
        "GenericHandlerWithOpenAndClosedCommandInterface",
        """
            public sealed record StrCmd : RequestBase, ICommand<string>;
            public sealed record Fixed : RequestBase, ICommand<int>;

            [PulseHandler<StrCmd>]
            public sealed class G<T> : ICommandHandler<T, string>, ICommandHandler<Fixed, int>
                where T : ICommand<string>
            {
                public Task<string> HandleAsync(T command, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
                public Task<int> HandleAsync(Fixed command, CancellationToken cancellationToken = default) => Task.FromResult(0);
            }
            """,
        "ICommandHandler<global::StrCmd, string>, global::G<global::StrCmd>>",
        null
    )]
    [Arguments(
        "ConcreteHandlerForSecondResultOfMessageWithSeveralResults",
        """
            public sealed record Both : RequestBase, ICommand<string>, ICommand<int>;

            [PulseHandler<Both>]
            public sealed class H : ICommandHandler<Both, int>
            {
                public Task<int> HandleAsync(Both command, CancellationToken cancellationToken = default) => Task.FromResult(0);
            }
            """,
        "ICommandHandler<global::Both, int>, global::H>",
        null
    )]
    [Arguments(
        "ConcreteHandlerWithDifferentTupleElementNames",
        """
            public sealed record TupleCmd : RequestBase, ICommand<(int A, int B)>;

            [PulseHandler<TupleCmd>]
            public sealed class H : ICommandHandler<TupleCmd, (int X, int Y)>
            {
                public Task<(int X, int Y)> HandleAsync(TupleCmd command, CancellationToken cancellationToken = default) => Task.FromResult((0, 0));
            }
            """,
        "ICommandHandler<global::TupleCmd, (int X, int Y)>, global::H>",
        null
    )]
    [Arguments(
        "GenericHandlerWithSelfReferentialConstructedResult",
        """
            public sealed record Result<T>;
            public sealed record Msg : RequestBase, ICommand<Result<Msg>>;

            [PulseHandler<Msg>]
            public sealed class G<T> : ICommandHandler<T, Result<T>>
                where T : ICommand<Result<T>>
            {
                public Task<Result<T>> HandleAsync(T command, CancellationToken cancellationToken = default) => Task.FromResult(new Result<T>());
            }
            """,
        "ICommandHandler<global::Msg, global::Result<global::Msg>>, global::G<global::Msg>>",
        null
    )]
    public async Task WhenHandlerImplementsSameHandlerInterfaceForSeveralMessagesThenEachExplicitMessageTypeIsRegistered(
        string scenario,
        string declarations,
        string expectedRegistration,
        string? secondExpectedRegistration
    )
    {
        var result = RunGenerator(declarations);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result.InputErrors).IsEmpty().Because(scenario);
            _ = await Assert.That(result.PulseDiagnostics).IsEmpty().Because(scenario);
            _ = await Assert.That(result.GeneratedErrors).IsEmpty().Because(scenario);
            _ = await Assert.That(result.GeneratedSource).Contains(expectedRegistration).Because(scenario);
            if (secondExpectedRegistration is not null)
            {
                _ = await Assert.That(result.GeneratedSource).Contains(secondExpectedRegistration).Because(scenario);
            }
        }
    }

    [Test]
    [Arguments(
        "GenericHandlerWhereOnlyClosedInterfaceMatches",
        """
            public sealed record Fixed : RequestBase, ICommand<int>;

            [PulseHandler<Fixed>]
            public sealed class G<T> : ICommandHandler<T, string>, ICommandHandler<Fixed, int>
                where T : ICommand<string>
            {
                public Task<string> HandleAsync(T command, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
                public Task<int> HandleAsync(Fixed command, CancellationToken cancellationToken = default) => Task.FromResult(0);
            }
            """,
        "ICommandHandler<global::Fixed, string>"
    )]
    [Arguments(
        "GenericHandlerWithOpenInterfaceOfOtherResultTypeAndMessageWithSeveralResults",
        """
            public sealed record Both : RequestBase, ICommand<string>, ICommand<int>;

            [PulseHandler<Both>]
            public sealed class G<T> : ICommandHandler<T, int>, ICommandHandler<Both, string>
                where T : ICommand<int>
            {
                public Task<int> HandleAsync(T command, CancellationToken cancellationToken = default) => Task.FromResult(0);
                public Task<string> HandleAsync(Both command, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
            }
            """,
        "ICommandHandler<global::Both, int>"
    )]
    [Arguments(
        "ConcreteHandlerWithoutInterfaceForMessage",
        """
            public sealed record CmdA : RequestBase, ICommand<string>;
            public sealed record CmdB : RequestBase, ICommand<string>;

            [PulseHandler<CmdB>]
            public sealed class H1 : ICommandHandler<CmdA, string>
            {
                public Task<string> HandleAsync(CmdA command, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
            }
            """,
        "global::CmdB"
    )]
    public async Task WhenNoImplementedHandlerInterfaceFitsMessageAndResultTypeThenPulse006Reported(
        string scenario,
        string declarations,
        string forbiddenRegistration
    )
    {
        var result = RunGenerator(declarations);

        using (Assert.Multiple())
        {
            _ = await Assert.That(result.InputErrors).IsEmpty().Because(scenario);
            _ = await Assert
                .That(result.PulseDiagnostics.Select(d => d.Id))
                .IsEquivalentTo(["PULSE006"])
                .Because(scenario);
            _ = await Assert.That(result.GeneratedErrors).IsEmpty().Because(scenario);
            _ = await Assert.That(result.GeneratedSource).DoesNotContain(forbiddenRegistration).Because(scenario);
        }
    }

    private static GeneratorResult RunGenerator(string declarations)
    {
        var references = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string)!
            .Split(Path.PathSeparator)
            .Where(path =>
            {
                var fileName = Path.GetFileName(path);
                return fileName.StartsWith("System.", StringComparison.Ordinal)
                    || fileName.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal)
                    || string.Equals(fileName, "NetEvolve.Pulse.Extensibility.dll", StringComparison.Ordinal)
                    || string.Equals(fileName, "netstandard.dll", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(fileName, "mscorlib.dll", StringComparison.OrdinalIgnoreCase);
            })
            .Select(path => MetadataReference.CreateFromFile(path));

        var inputTree = CSharpSyntaxTree.ParseText(
            Preamble + declarations,
            new CSharpParseOptions(LanguageVersion.Latest)
        );
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [inputTree],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );

        _ = CSharpGeneratorDriver
            .Create(
                generators: [new PulseHandlerGenerator().AsSourceGenerator()],
                optionsProvider: new TestAnalyzerConfigOptionsProvider("TestAssembly")
            )
            .RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);

        var pulseDiagnostics = generatorDiagnostics
            .Where(d => d.Id.StartsWith("PULSE", StringComparison.Ordinal))
            .ToArray();
        var errors = outputCompilation
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Location.SourceTree is not null)
            .ToArray();
        var generatedSource = string.Concat(
            outputCompilation.SyntaxTrees.Where(tree => tree != inputTree).Select(tree => tree.ToString())
        );

        return new GeneratorResult(
            pulseDiagnostics,
            [.. errors.Where(d => d.Location.SourceTree == inputTree)],
            [.. errors.Where(d => d.Location.SourceTree != inputTree)],
            generatedSource
        );
    }

    private sealed record GeneratorResult(
        Diagnostic[] PulseDiagnostics,
        Diagnostic[] InputErrors,
        Diagnostic[] GeneratedErrors,
        string GeneratedSource
    );
}
