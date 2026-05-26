# U05 Verification

**Status:** CONFIRMED

**Evidence:** `src/NetEvolve.Pulse/Outbox/OutboxProcessorOptions.cs:10-99` (no validation attributes / no `IValidateOptions<T>` companion); `src/NetEvolve.Pulse/OutboxExtensions.cs:51-61` (`AddOutbox` calls `AddOptions<OutboxProcessorOptions>()` then `services.Configure(...)` — no `.Validate(...)`, no `.ValidateDataAnnotations()`, no `.ValidateOnStart()`); only registered validator is `src/NetEvolve.Pulse/Interceptors/LoggingInterceptorOptionsValidator.cs` (confirmed by `Grep "IValidateOptions" src/` matching only that file + the registration in `LoggingExtensions.cs`).

**Reasoning:**
`OutboxProcessorOptions` exposes a wide surface of numerically- and temporally-meaningful properties (`BatchSize`, `PollingInterval`, `BackoffMultiplier`, `ProcessingTimeout`, `BaseRetryDelay`, `MaxRetryDelay`, `MaxRetryCount`) yet none are guarded by data annotations or an `IValidateOptions<OutboxProcessorOptions>`. `AddOutbox()` does not call `ValidateOnStart()`. Consequence: `BatchSize=0` resolves cleanly, `PollingInterval=TimeSpan.Zero` resolves cleanly, etc., and the processor only fails far inside the loop or — worse — never fails at all (e.g., `BatchSize=0` simply yields no work). This is the documented fail-fast gap.

**Failing test / repro (if confirmed):**
- Path: `tests/NetEvolve.Pulse.Tests.Unit/Audit/U05_OutboxProcessorOptionsValidationTests.cs`
- Status: written, builds, and fails as expected (4/4 failing)
- Code or steps:
```text
dotnet run --no-build -c Debug -f net10.0 \
  --project tests/NetEvolve.Pulse.Tests.Unit \
  -- --treenode-filter "/*/*/U05_OutboxProcessorOptionsValidationTests/*"

# Result:
#   gesamt: 4
#   fehlerhaft: 4
# Each test reports:
#   "Expected to throw OptionsValidationException, because AddOutbox() must register
#    an IValidateOptions<OutboxProcessorOptions> that rejects <field>=<bad-value>."
#    but no exception was thrown
```

**Notes:**
- The four scenarios covered match the assumption text: `BatchSize=0`, `PollingInterval=TimeSpan.Zero`, `BackoffMultiplier=0`, `ProcessingTimeout=TimeSpan.Zero`.
- Tests use `services.BuildServiceProvider(validateScopes: true)` and resolve `IOptions<OutboxProcessorOptions>.Value` — the standard mechanism that surfaces `OptionsValidationException` from a registered `IValidateOptions<T>` (see `LoggingInterceptorOptionsValidator` for the reference pattern).
- Phase 3 fix: add `OutboxProcessorOptionsValidator : IValidateOptions<OutboxProcessorOptions>`, register it inside `AddOutbox()` via `services.AddSingleton<IValidateOptions<OutboxProcessorOptions>, OutboxProcessorOptionsValidator>()`, and chain `.ValidateOnStart()` on the options builder so misconfig fails the host at startup, not at first dispatch.
- `AzureServiceBusTransportOptions` (audit text mentions it as a counter-example of "validation runs inline at first resolution") was not exercised by this round — that should be a separate U05-follow-up scenario in a later round.
