# Mikan Download Source Audit: Top 4 Issues (Root Cause, Behavior Mismatch, and Fixes)

This document captures the first 4 issues from the audit, with concrete root causes, examples of code behavior that does not match expected behavior, and practical fix options.

## 1) Concurrent `DbContext` usage in `MikanClient` cache write path

### Root cause
- In `GetParsedFeedAsync`, feed cache and subgroup cache writes are started in parallel:
  - `backend/Services/Implementations/MikanClient.cs:709`
  - `backend/Services/Implementations/MikanClient.cs:710`
  - `backend/Services/Implementations/MikanClient.cs:711`
- Both tasks use the same injected `_dbContext`, and both call `SaveChangesAsync`:
  - `backend/Services/Implementations/MikanClient.cs:1013`
  - `backend/Services/Implementations/MikanClient.cs:1065`
- EF Core `DbContext` is not thread-safe.

### Behavior mismatch example
- Expected behavior: first-time feed refresh writes both feed items and subgroup mapping successfully.
- Actual code behavior: parallel writes on one context can trigger runtime failure (for example, "A second operation was started on this context..."), causing request failure or stale-cache fallback.

### Proposed solution
- Do not write both caches concurrently on the same context.
- Recommended implementation:
  - Serialize writes: `await UpsertCachedFeedAsync(...)` then `await UpsertCachedSubgroupMappingAsync(...)`.
- Alternative:
  - Use separate scoped `DbContext` instances for each write task if parallelism is required.

## 2) Polling flow can permanently skip items after qB add failure

### Root cause
- In `CheckSubscriptionInternalAsync`, history is inserted before qB add:
  - Create history object: `backend/Services/Implementations/SubscriptionService.cs:568`
  - Persist history: `backend/Services/Implementations/SubscriptionService.cs:584`
  - Then call qB add: `backend/Services/Implementations/SubscriptionService.cs:591`
- If qB add throws, catch only logs and continues:
  - `backend/Services/Implementations/SubscriptionService.cs:619`
- Later polls dedupe by existing hash in DB:
  - `backend/Services/Repositories/SubscriptionRepository.cs:147`

### Behavior mismatch example
- Scenario:
  - Poll discovers hash `H1`.
  - History row for `H1` is saved.
  - qB is temporarily unavailable, add throws.
  - Next poll sees `H1` already in DB and treats it as already processed.
- Expected behavior: temporary qB outage should allow automatic retry later.
- Actual code behavior: item is effectively dead-lettered and may never auto-download.

### Proposed solution
- Make DB history and qB submission consistent.
- Practical options:
  - Option A (simple): add to qB first; only persist history as downloaded/downloading if qB succeeds.
  - Option B (more robust): keep pre-insert, but add explicit states (for example `QueuedForPush` / `PushFailed`) and retry logic; dedupe should only block hashes with successful push states, not failed transient states.
  - Option C: wrap with transactional outbox pattern to ensure retryable delivery.

## 3) Subgroup mapping cache is append/update only; stale mappings are never pruned

### Root cause
- Cached subgroup mappings are always read back in full:
  - `backend/Services/Implementations/MikanClient.cs:1023`
- Upsert path updates/inserts only, no deletion for removed subgroups.
- If latest scrape returns empty list, function returns immediately:
  - `backend/Services/Implementations/MikanClient.cs:1036`

### Behavior mismatch example
- Expected behavior: if a subgroup disappears from Mikan page, API response should eventually stop returning it.
- Actual code behavior: old subgroup rows remain in `MikanSubgroups`, so API keeps returning stale subgroup options.

### Proposed solution
- Turn subgroup persistence into a full-sync operation per `mikanBangumiId`:
  - Upsert current rows.
  - Delete rows not present in current scrape result.
- When scrape returns empty:
  - Decide policy explicitly:
    - Either clear all rows for this bangumi (strict sync), or
    - mark scrape failure and keep old rows only when fetch actually failed (not when fetch succeeded with empty result).

## 4) Search retry strategy aborts too early on first request/parsing error

### Root cause
- `SearchAnimeAsync` iterates multiple query strategies, but returns immediately on first exception:
  - `backend/Services/Implementations/MikanClient.cs:239`
  - `backend/Services/Implementations/MikanClient.cs:247`
- This prevents trying fallback query modes in the same search request.

### Behavior mismatch example
- Expected behavior: if one query mode fails (transient HTTP error or parsing issue), remaining query modes should still be attempted.
- Actual code behavior: first exception returns `null` immediately; all fallback strategies are skipped.

### Proposed solution
- Change per-query exception handling from `return null` to `continue`.
- Keep loop-level behavior:
  - Continue trying next query candidate.
  - If all fail, return `null`.
- Optional improvement:
  - Collect per-mode errors and log a final aggregated warning for debugging.

