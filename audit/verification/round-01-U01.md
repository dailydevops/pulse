# U01 Verification

**Status:** CONFIRMED

**Evidence:** `README.md:118-136` (Quick Use snippet); `src/NetEvolve.Pulse.Extensibility/ICommand`1.cs` (declares `ICommand<TResponse>` which inherits `IRequest<TResponse>` carrying `CorrelationId` / `CausationId` interface members).

**Reasoning:**
The Quick Use snippet declares `public record CreateOrder(string Sku) : ICommand<OrderCreated>;` without implementing `CorrelationId` or `CausationId` properties required by the `IRequest<TResponse>` parent of `ICommand<TResponse>`. Pasting the snippet verbatim into a real `net10.0` console project that project-references `NetEvolve.Pulse` and `NetEvolve.Pulse.Extensibility` produces two `CS0535` compile errors. The snippet additionally never calls `BuildServiceProvider()` and never dispatches anything, so even if `CS0535` were removed it would be inert as a first-impression sample.

**Failing test / repro (if confirmed):**
- Path: `audit/repros/u01/`
- Status: written
- Code or steps:
```text
cd audit/repros/u01
dotnet build U01Repro.csproj

# Result:
# Program.cs(13,41): error CS0535: 'CreateOrder' does not implement interface member 'IRequest<OrderCreated>.CausationId'
# Program.cs(13,41): error CS0535: 'CreateOrder' does not implement interface member 'IRequest<OrderCreated>.CorrelationId'
```

**Notes:**
- Exact CS errors captured: `CS0535` x2 (CausationId, CorrelationId).
- Secondary defect: snippet shows neither `BuildServiceProvider()` nor `IMediator.SendAsync(...)` — even if compile errors are fixed, sample never dispatches anything, undermining "first impression" value.
- Counter-example at `src/NetEvolve.Pulse/README.md:42-72` does show a working dispatch pattern; root README should be aligned with it.
