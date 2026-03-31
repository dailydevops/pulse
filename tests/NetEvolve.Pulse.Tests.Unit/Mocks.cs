using Microsoft.AspNetCore.Http;
using NetEvolve.Pulse.Extensibility;

[assembly: GenerateMock(typeof(IHttpContextAccessor))]
[assembly: GenerateMock(typeof(IMediatorConfigurator))]
