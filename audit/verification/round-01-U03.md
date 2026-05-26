# U03 Verification

**Status:** CONFIRMED

**Evidence:** `src/NetEvolve.Pulse/AssemblyScanningExtensions.cs:15` — XML doc reads: *"…use source generator-based registration instead by referencing the `NetEvolve.Pulse.Generators` package."*; `src/NetEvolve.Pulse.SourceGeneration/NetEvolve.Pulse.SourceGeneration.csproj` (the actual source-gen project — no explicit `<PackageId>`, so PackageId == AssemblyName == `NetEvolve.Pulse.SourceGeneration`); compiled XML doc at `src/NetEvolve.Pulse/bin/Debug/net10.0/NetEvolve.Pulse.xml:31` reproduces the wrong id.

**Reasoning:**
There is no `src/NetEvolve.Pulse.Generators` project anywhere in the repo. The only source-generator project is `NetEvolve.Pulse.SourceGeneration` (csproj inherits AssemblyName from project name, so the published NuGet package id matches the folder name). A user following the XML doc and running `dotnet add package NetEvolve.Pulse.Generators` will install something that does not exist — or, worse, install a future typo-squat package once someone notices.

**Failing test / repro (if confirmed):**
- Path: `tests/NetEvolve.Pulse.Tests.Unit/Audit/U03_AssemblyScanningPackageNameTests.cs`
- Status: written
- Code or steps:
```csharp
// Reflectively loads NetEvolve.Pulse.xml next to NetEvolve.Pulse.dll and asserts the
// T:NetEvolve.Pulse.AssemblyScanningExtensions <member> recommends "NetEvolve.Pulse.SourceGeneration"
// (and does not mention the phantom "NetEvolve.Pulse.Generators").
//
// Today both assertions fail; Phase 3 must fix the XML doc.
```

**Notes:**
- Test reads the on-disk XML via `Path.Combine(Path.GetDirectoryName(typeof(AssemblyScanningExtensions).Assembly.Location), "NetEvolve.Pulse.xml")` — the XML file is auto-copied next to the assembly via the project reference, no extra MSBuild plumbing needed.
- Same wording appears in the compiled XML (verified at `src/NetEvolve.Pulse/bin/Debug/net10.0/NetEvolve.Pulse.xml:31`).
- Phase 3 fix is a one-line C# XML-doc edit on `AssemblyScanningExtensions.cs:15` (`Generators` → `SourceGeneration`).
