---
authors:
  - Martin Stühmer

applyTo:
  - "src/NetEvolve.Pulse/CacheInvalidationMediatorBuilderExtensions.cs"
  - "src/NetEvolve.Pulse/Interceptors/CacheInvalidationInterceptor*.cs"
  - "src/NetEvolve.Pulse/Interceptors/DistributedCacheQueryInterceptor*.cs"
  - "src/NetEvolve.Pulse/Internals/*CacheKeyRegistry.cs"

created: 2026-09-29

lastModified: 2026-09-29

state: accepted

instructions: |
  The cache key registry used by AddCacheInvalidation MUST stay process-local and in-memory; IInvalidatingCommand evicts only the keys this process cached since it started.
  MUST document that limitation on AddCacheInvalidation, the invalidation and query caching interceptors and the NetEvolve.Pulse README.
  MUST NOT add a distributed invalidation mechanism (generation token, HybridCache tags, pub/sub) without a superseding decision.
---

# Decision: Process-Local Cache Invalidation

`AddCacheInvalidation` keeps its in-memory, per-process cache key registry. Cross-process and cross-restart invalidation is documented as a limitation and is not implemented for now.

## Context

Issue #829 shows that `IInvalidatingCommand` evicts only the entries that the current process cached and registered since it started. `DistributedCacheQueryInterceptor` stores results in `IDistributedCache`, which other instances share and which survives restarts. The key registry that `CacheInvalidationInterceptor` reads is an in-memory singleton, so:

- a command handled on instance B does not evict entries that instance A cached;
- entries cached before a restart are never evicted by a command. They leave the cache only when they expire.

`IDistributedCache` cannot enumerate keys or remove entries by tag or prefix. The issue lists a per-type distributed generation token as an optional follow-up, not as a requirement. The concurrency and growth defects of the registry are fixed separately, without depending on this decision.

## Decision

- The registry stays in-memory and local to the process.
- The limitation is documented on `AddCacheInvalidation`, `CacheInvalidationInterceptor`, `DistributedCacheQueryInterceptor` and in `src/NetEvolve.Pulse/README.md`. The documentation recommends setting `ICacheableQuery<TResponse>.Expiry` or `QueryCachingOptions.DefaultExpiry` so that entries missed by invalidation expire eventually.

## Consequences

- No new dependency and no additional cache round trip per query.
- Single-instance deployments get reliable invalidation for the lifetime of the process.
- Multi-instance deployments, and entries that outlive a restart, can serve stale data until they expire. Without an expiry they are stale indefinitely.

## Alternatives Considered

- **Per-type generation token folded into the cache key.** A counter per query type is stored in `IDistributedCache`, and the query interceptor adds it to every key. An invalidating command increments the counter, so every instance misses on its next read. This works with any `IDistributedCache`, but costs an extra cache read per query. `IDistributedCache` has no atomic increment, so concurrent increments need a random token instead of a counter. Old entries stay in the cache until they expire. It also changes the cache key format, which is a visible behaviour change. Deferred until there is a concrete multi-instance need.
- **`HybridCache` with tags and `RemoveByTagAsync`.** Tag invalidation is logical: entries created before the invalidation are treated as misses, including in the shared L2 cache. The in-memory L1 cache of other servers is not affected, according to the Microsoft Learn documentation of `HybridCache`. Adopting it means replacing the `IDistributedCache`-based interceptor with a `HybridCache`-based one, which is a larger redesign.
- **Pub/sub (for example Redis) to broadcast invalidations.** This reaches running instances, but not entries whose keys no running instance registered, such as entries cached before a restart. It also ties the core package to a transport.

## Related Decisions (Optional)

None at this time.
