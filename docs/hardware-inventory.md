# Host hardware inventory

Connector protocol 6 carries PitCrew manager contract 13 `host.hardware`
inventory. The dashboard validates and stores the exact sanitized contract; it
does not enrich processor names from an external database or infer
performance- and efficiency-core topology.

## Current state

Node Detail shows:

- processor model and manager-normalized architecture;
- physical and logical core counts when observable;
- performance and efficiency core counts only when the manager reported them;
- Docker-visible memory;
- operating-system and kernel identity;
- Docker server, storage driver, and backing filesystem;
- collection status, timestamps, and inventory hash.

`current` means the latest bounded manager collection succeeded. `stale` shows
the last valid values after a failed refresh. `unavailable` contains no
retained values. Older connectors and manager contracts show hardware as
unreported rather than as zero.

Profile projections omit the duplicated `host` envelope after ingestion. The
validated node-level projection is the authoritative current API surface.

The fleet overview can compare up to four selected nodes. The comparison uses
reported values only and does not rank processors or estimate performance.

## History

SQLite stores one node revision per contiguous inventory episode. Identical
reports from several profiles or consecutive synchronizations update the same
revision rather than duplicating it. A later return to an older hash creates a
new timestamped revision so `A -> B -> A` and `A -> unavailable -> A`
transitions remain visible.
Hardware revisions:

- are stored separately from high-frequency profile telemetry;
- appear in node history at their first dashboard observation;
- use the diagnostic history range and node/database row ceilings;
- report node and database incompleteness floors when retention deletes
  episodes;
- retain first and most recent observation times plus the latest reporting
  profile.

An unavailable report updates current state but does not invent a revision.

## Privacy boundary

The dashboard accepts only the bounded contract from PitCrew. It never stores
or displays usernames, absolute host paths, serial numbers, machine GUIDs,
MAC addresses, IP addresses, Docker root paths, credentials, runner
registration material, or job output.

Diagnostic credentials without profile restrictions can read the same current
and node-history hardware projection when their tenant and node restrictions
permit it. Profile-restricted credentials and profile-history routes omit
node-wide hardware evidence, including hardware retention-loss counts, rather
than exposing data outside that scope.
