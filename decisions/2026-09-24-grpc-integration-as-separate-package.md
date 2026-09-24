---
authors:
  - Martin Stühmer

applyTo:
  - "src/NetEvolve.Pulse.AspNetCore/**"
  - "src/NetEvolve.Pulse.AspNetCore.Grpc/**"
  - "Directory.Packages.props"

created: 2026-09-24

lastModified: 2026-09-24

state: proposed

instructions: |
  gRPC integration for Pulse lives in the separate package NetEvolve.Pulse.AspNetCore.Grpc, which depends on Grpc.AspNetCore.Server; NetEvolve.Pulse.AspNetCore MUST NOT reference any Grpc.* package.
  Grpc.* package versions MUST stay aligned with the Grpc.* versions pulled in transitively by other dependencies (e.g. Dapr.Client) to avoid NU1605/NU1608 under central transitive pinning.
---

# Decision: gRPC Integration as a Separate Package

Server-streaming gRPC support for Pulse streaming queries (`PulseGrpcStreamService<TQuery, TResponse>`, `MapStreamQueryGrpc<TService>()`) is shipped in a new package, `NetEvolve.Pulse.AspNetCore.Grpc`, instead of the existing `NetEvolve.Pulse.AspNetCore` package.

## Context

Issue [#299](https://github.com/dailydevops/pulse/issues/299) asks for a base class that writes `IMediator.StreamQueryAsync` results to an `IServerStreamWriter<TResponse>`, plus an endpoint mapping extension. Both need types from `Grpc.Core.Api` and `Grpc.AspNetCore.Server`.

`NetEvolve.Pulse.AspNetCore` only depends on the ASP.NET Core shared framework and `NetEvolve.Pulse.Extensibility`. Most consumers use it for Minimal API endpoints and never use gRPC. If the gRPC types went into that package, every consumer would get `Grpc.AspNetCore.Server`, `Grpc.Net.Common` and `Grpc.Core.Api` as transitive dependencies.

The repository uses central package management with `CentralPackageTransitivePinningEnabled`. `Dapr.Client` already pulls in `Grpc.Net.Client` 2.80.0, so any new `Grpc.*` reference has to use a matching version.

## Decision

- Create `src/NetEvolve.Pulse.AspNetCore.Grpc` with the same target frameworks, root namespace (`NetEvolve.Pulse`) and packaging metadata as the sibling packages.
- The package references `Grpc.AspNetCore.Server` rather than the `Grpc.AspNetCore` metapackage. The metapackage would also pull in `Google.Protobuf` and `Grpc.Tools`, which are the consumer's choice.
- `Grpc.AspNetCore.Server` is pinned to 2.80.0 to match the `Grpc.*` version that `Dapr.Client` brings in.
- `MapStreamQueryGrpc` takes the concrete service type (`MapStreamQueryGrpc<TService>()`) instead of the `<TQuery, TResponse>` signature proposed in the issue. `MapGrpcService<TService>()` can only bind a concrete service type that carries a `BindServiceMethodAttribute`.

## Consequences

### Positive

- `NetEvolve.Pulse.AspNetCore` stays free of gRPC dependencies.
- gRPC users opt in explicitly by referencing one extra package.
- Each package can evolve and be versioned independently.

### Negative

- There is one more package to build, document and release.
- C# allows only one base class, so a `PulseGrpcStreamService` subclass cannot also derive from a Grpc.Tools-generated `XxxBase` class. Consumers add a one-line static `BindService` bridge that forwards to the generated `Xxx.BindService(binder, null)` (see the package README).
- The base class writes `TResponse` items as they are. Mapping to Protobuf messages happens in the query or its handler, not in the base class.

## Alternatives Considered

- **Add the types to `NetEvolve.Pulse.AspNetCore`** (the path named in the issue). Rejected: this forces a gRPC dependency on every ASP.NET Core consumer.
- **Reference `Grpc.AspNetCore`** (the metapackage). Rejected: it also brings in `Google.Protobuf` and `Grpc.Tools`, which consumers should add themselves.
- **Keep the `<TQuery, TResponse>` signature** from the issue. Rejected: it cannot work, because gRPC endpoint binding needs a concrete service type.

## Related Decisions

- [Centralized Package Version Management](./2025-07-10-centralized-package-version-management.md) - the new `Grpc.AspNetCore.Server` version is managed centrally.
- [Folder Structure and Naming Conventions](./2025-07-10-folder-structure-and-naming-conventions.md) - the new project follows the `src/NetEvolve.Pulse.*` layout.
- [NuGet Package README Template](./2026-01-09-nuget-package-readme-template.md) - the new package README follows the template.
