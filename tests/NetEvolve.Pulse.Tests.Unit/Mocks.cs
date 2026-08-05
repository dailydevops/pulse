using Microsoft.AspNetCore.Http;
using NetEvolve.Pulse.Extensibility;
using NetEvolve.Pulse.Extensibility.Audit;
using NetEvolve.Pulse.Extensibility.DeadLetter;
using NetEvolve.Pulse.Extensibility.Outbox;
using StackExchange.Redis;

[assembly: GenerateMock(typeof(IHttpContextAccessor))]
[assembly: GenerateMock(typeof(IConnectionMultiplexer))]
[assembly: GenerateMock(typeof(IMediatorBuilder))]
[assembly: GenerateMock(typeof(IOutboxManagement))]
[assembly: GenerateMock(typeof(ICommandDeadLetterManagement))]
[assembly: GenerateMock(typeof(IAuditManagement))]
