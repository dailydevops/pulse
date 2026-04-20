using Microsoft.AspNetCore.Http;
using NetEvolve.Pulse.Extensibility;
using StackExchange.Redis;

[assembly: GenerateMock(typeof(IHttpContextAccessor))]
[assembly: GenerateMock(typeof(IConnectionMultiplexer))]
[assembly: GenerateMock(typeof(IMediatorBuilder))]
