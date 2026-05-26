# U02 Verification

**Status:** CONFIRMED

**Evidence:** `src/NetEvolve.Pulse/README.md:152-162` references `options.JsonSerializerOptions`; `src/NetEvolve.Pulse.Extensibility/Caching/QueryCachingOptions.cs:10-59` defines only `ExpirationMode` (line 36) and `DefaultExpiry` (line 58). Root `README.md:59` likewise claims "configurable JsonSerializerOptions … via QueryCachingOptions".

**Reasoning:**
The Distributed Query Caching snippet in `src/NetEvolve.Pulse/README.md` assigns `options.JsonSerializerOptions = new JsonSerializerOptions { … }`, but the `QueryCachingOptions` type exposes no such member. Pasting the snippet verbatim into a real `net10.0` console project that project-references `NetEvolve.Pulse` and `NetEvolve.Pulse.Extensibility` produces `CS1061`. Serialization options for the cache pipeline are in fact controlled via the global `IPayloadSerializer` / `IOptions<JsonSerializerOptions>` registrations, not per-`AddQueryCaching` configuration.

**Failing test / repro (if confirmed):**
- Path: `audit/repros/u02/`
- Status: written
- Code or steps:
```text
cd audit/repros/u02
dotnet build U02Repro.csproj

# Result:
# Program.cs(22,17): error CS1061: 'QueryCachingOptions' does not contain a definition for 'JsonSerializerOptions'
#                                  and no accessible extension method 'JsonSerializerOptions' accepting a first argument
#                                  of type 'QueryCachingOptions' could be found
```

**Notes:**
- Exact CS error captured: `CS1061` x1 at Program.cs:22:17 (the `options.JsonSerializerOptions` access).
- Phase 3 fix options: (a) add `JsonSerializerOptions` to `QueryCachingOptions` and wire it through the caching interceptor, OR (b) update both README files to point users at the global `services.Configure<JsonSerializerOptions>(…)` / custom `IPayloadSerializer` registration paths.
- Root README:59 ("configurable JsonSerializerOptions … via QueryCachingOptions") needs the same alignment.
