# Data access and repositories

Repositories own persistence mechanics for one feature boundary. They translate
between storage rows and immutable domain values while keeping transactions, caching,
tracing, and provider-specific representation decisions explicit.

## Connections and round trips

Inject a connection factory so each operation owns connection lifetime. A repository
method should normally complete its lookup in one query using joins, CTEs, subqueries,
multi-result queries, or a batch predicate.

Multiple queries can be justified when one query would create an unstable Cartesian
product, provider limitation, or materially worse plan. The tradeoff should be visible
rather than hidden in a loop.

## Result contracts

Repository return types distinguish:

- value or error;
- value, null, or error;
- success or failure without a value.

The shared `Try` helpers provide consistent exception capture, logging, and telemetry.
Hand-written catch-to-result conversions drift from that behavior.

## Mapping and feature ownership

Database DTOs are private storage shapes. Repositories convert them to immutable domain
objects before returning.

A feature does not write another feature's tables. Cross-feature behavior goes through
the owning repository/SDK boundary. Cross-domain joins can be appropriate for
background/reporting queries, but hot cached paths should account for bypassed cache
ownership and freshness.

## Transactions

When callers may already own a transaction, the repository exposes:

1. a transaction overload containing the SQL;
2. a no-transaction overload that opens a connection, begins a transaction, delegates,
   and commits.

Both public operations remain individually traced. A private shared executor would
hide one operation from tracing and make transaction ownership less obvious.

## Caching

Cached reads use one atomic `GetOrSetAsync` factory so concurrent misses do not create a
stampede. A manual get-then-set sequence has a race between the two calls.

Writes invalidate every affected key after persistence succeeds. Cache policy is owned
by the feature whose data is cached.

## SQLite UUID representation

SQLite has no UUID type. Text and blob representations do not compare equal, and blob
byte order is driver-specific unless pinned.

Choose one representation for the database and route every read/write/query parameter
through one codec. Time-ordered UUIDs stored as blobs use big-endian RFC field order so
the stored bytes preserve ordering.

Round-trip a known UUID through the real provider and include non-zero high bytes so a
byte-order swap cannot pass accidentally.

## Repository tests

Repository tests resolve the SUT from the same generated service graph as production
and use the production database engine. In-memory replacements can hide SQL, collation,
transaction, and locking defects.

Unique test data prevents parallel collisions. Inline SQL is reserved for constructing
an otherwise unreachable invalid state; ordinary behavior is arranged and asserted
through first-class repositories.
