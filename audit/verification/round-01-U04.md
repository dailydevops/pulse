# U04 Verification

**Status:** CONFIRMED

**Evidence:** `src/NetEvolve.Pulse/Serialization/SystemTextJsonPayloadSerializer.cs:32-44` — every `Serialize` / `Deserialize` overload calls reflection-mode `JsonSerializer.Serialize<T>` / `Deserialize<T>` / `SerializeToUtf8Bytes<T>` with no `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]` annotations on the class or any method. The `IPayloadSerializer` contract (`src/NetEvolve.Pulse.Extensibility/IPayloadSerializer.cs:35-86`) likewise has no AOT-hostility attributes, so callers see a clean signature. AOT claim at `src/NetEvolve.Pulse/HandlerRegistrationExtensions.cs:8-17` advertises "All methods in this class are AOT-compatible and trimming-safe … No reflection is used at runtime".

**Reasoning:**
A consumer project with `<PublishAot>true</PublishAot>` + `<IsAotCompatible>true</IsAotCompatible>` + `<EnableAotAnalyzer>true</EnableAotAnalyzer>` + `<EnableTrimAnalyzer>true</EnableTrimAnalyzer>` that calls `IPayloadSerializer.Serialize(payload)` / `SerializeToBytes(payload)` builds with **zero IL2026 / IL3050 warnings**, even though those calls land in `SystemTextJsonPayloadSerializer` which uses untyped reflection-mode `System.Text.Json`. The audit assumption is correct: the AOT analyzer cannot warn the caller because the chain is unannotated, and at runtime AOT consumers will silently lose unreferenced payload type metadata.

**Failing test / repro (if confirmed):**
- Path: `audit/repros/u04/`
- Status: written
- Code or steps:
```text
cd audit/repros/u04
dotnet build U04AotRepro.csproj   # 0 Warnung(en), 0 Fehler — AOT analyzer is SILENT
dotnet publish U04AotRepro.csproj -r win-x64 -c Release --self-contained
# Native linker fails on machines without the C++ workload, but the trim/AOT analyzer
# stage runs first and still emits zero IL2026/IL3050 warnings. That silence is the bug.
```

**Notes:**
- Build output captured: `0 Warnung(en), 0 Fehler` on Debug build with `EnableTrimAnalyzer=true`.
- Phase 3 fix: annotate `SystemTextJsonPayloadSerializer.Serialize<T>` / `Deserialize<T>` / `SerializeToBytes<T>` with `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`, and likewise propagate to `IPayloadSerializer` so callers receive the warning at their call site. Alternatively introduce a source-generator-backed serializer for AOT scenarios and have the default DI registration prefer it when `RuntimeFeature.IsDynamicCodeSupported == false`.
- `dotnet publish -r win-x64 --self-contained` fails on this machine at the native linker step (missing C++ workload). The trim/AOT analyzer phase still runs first and emits 0 IL warnings — that's the load-bearing data point. Linker output: `error : Platform linker not found. Ensure you have all the required prerequisites …` is unrelated to the audit claim.
