# U08 Repro — `OutboxMessage.sql` not delivered via PackageReference

The SQL Server outbox README directs users to "execute the schema script from
`Scripts/OutboxMessage.sql`", but the script is packed only under the legacy
`content\Scripts\` path which does **not** flow to PackageReference consumers
(the modern SDK-style default). The README's documented remediation therefore
fails to deliver the file.

## Run the repro

```pwsh
pwsh ./repro.ps1
```

The script:

1. `dotnet pack`s `src/NetEvolve.Pulse.SqlServer` into a scratch folder.
2. Inspects the resulting `.nupkg` for `OutboxMessage.sql` entries.
3. Reports the discovered path(s).
4. Asserts that *at least one* of the discovered paths starts with
   `contentFiles/`, `build/`, or `buildTransitive/` (= PackageReference-reachable).

Today the only entry found is `content/Scripts/OutboxMessage.sql`, so the
assertion fails and the script exits non-zero.

Companion TUnit test:
`tests/NetEvolve.Pulse.Tests.Unit/SqlServer/SqlServerOutboxScriptPackagingTests.cs`.

## Why this matters

A consumer following the README literally:

```xml
<PackageReference Include="NetEvolve.Pulse.SqlServer" Version="*" />
```

builds, restores, and looks under `bin/`/`obj/`/`packages/` for the script and
finds **nothing**. The first event publish then throws
`Invalid object name 'pulse.OutboxMessage'`. The documented remediation does
not deliver the file.

## Fix sketch (out of scope — Phase 3)

Replace the packaging line in `src/NetEvolve.Pulse.SqlServer/NetEvolve.Pulse.SqlServer.csproj`:

```xml
<None Include="Scripts\*.sql"
      Pack="true"
      PackagePath="contentFiles\any\any\Scripts\;content\Scripts\"
      BuildAction="None"
      CopyToOutput="true" />
```

Or ship a `build\NetEvolve.Pulse.SqlServer.targets` that copies the script as
a build event. The script is already an `EmbeddedResource`; a third option is
to expose a helper that writes it to disk via
`Assembly.GetManifestResourceStream`.
