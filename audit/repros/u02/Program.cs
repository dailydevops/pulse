// Verbatim copy of src/NetEvolve.Pulse/README.md lines 152-162 (Distributed Query Caching snippet).
// This file exists to prove the snippet does not compile (U02 audit assumption).

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NetEvolve.Pulse;
using NetEvolve.Pulse.Extensibility.Caching;

var services = new ServiceCollection();

// 1. Register an IDistributedCache implementation
services.AddDistributedMemoryCache(); // or Redis, SQL Server, etc.

// 2. Enable the caching interceptor (with optional options)
services.AddPulse(config =>
    config.AddQueryCaching(options =>
    {
        // Choose between absolute (default) and sliding expiration
        options.ExpirationMode = CacheExpirationMode.Sliding;

        // Supply custom JsonSerializerOptions for cache serialization
        options.JsonSerializerOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    })
);
