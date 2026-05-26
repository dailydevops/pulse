// U04 — Default JSON serializer breaks AOT silently.
//
// This program references NetEvolve.Pulse from an AOT-enabled console (PublishAot=true).
// The intent is to demonstrate that the chain `IPayloadSerializer -> SystemTextJsonPayloadSerializer
// -> JsonSerializer.Serialize<T>` produces NO IL2026 / IL3050 warning at compile or publish time,
// even though it uses reflection-mode serialization, because `SystemTextJsonPayloadSerializer`
// lacks `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]` annotations and does not flow them
// to its `IPayloadSerializer` contract.
//
// Reproduction:
//   dotnet publish audit/repros/u04/U04AotRepro.csproj -r win-x64 -c Release
//
// Expected (after Phase 3 fix):  IL2026 / IL3050 warnings on the Serialize call sites below.
// Actual (today):                build/publish completes cleanly. Audit assumption confirmed.

using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility;

var services = new ServiceCollection();
services.AddPulse();

await using var provider = services.BuildServiceProvider();

var serializer = provider.GetRequiredService<IPayloadSerializer>();

// These two calls hit reflection-mode JsonSerializer under the hood (via
// SystemTextJsonPayloadSerializer). On an AOT-annotated chain the analyzer should warn here.
var payload = new DemoPayload("u04", 42);
var s1 = serializer.Serialize(payload);
var b1 = serializer.SerializeToBytes(payload);

System.Console.WriteLine(s1);
System.Console.WriteLine(b1.Length);

public sealed record DemoPayload(string Name, int Value);
