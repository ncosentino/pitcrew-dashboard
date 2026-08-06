# Runner correlation assignments

Connector protocol 7 carries PitCrew manager contract 14
`slots[].runnerNameHash`. The value is the lowercase SHA-256 digest of the
exact GitHub runner name. Dashboard never receives or stores the raw runner
name, configured prefix, container identity, registration payload, JIT
configuration, token, or job output.

## Exact matching

A diagnostic client:

1. reads the exact `runner_name` from GitHub Actions job metadata;
2. hashes its UTF-8 bytes with SHA-256;
3. compares the lowercase hexadecimal digest with retained assignments.

The hash is an equality correlation key, not authentication material and not a
fuzzy host identifier. Missing or ambiguous matches remain unavailable.

Each retained interval includes:

- runner-name hash;
- node and profile;
- stable manager slot key;
- sanitized repository and autoscaling target when reported;
- first and most recent Dashboard observation times.

Connector protocol 8 enriches an assignment with manager contract 15 job
context when available:

- canonical GitHub repository URL;
- workflow-run and job identifiers;
- bounded display and event names;
- queue, assignment, observed start, and finish timestamps;
- bounded completion result.

The dashboard derives an exact GitHub job link. It does not store a GitHub
Actions write credential, cancel jobs, or retain workflow refs, labels,
payloads, logs, step output, environment values, or raw runner identity.

Repeated heartbeats extend one interval. A new ephemeral runner name creates a
new assignment.

## History and retention

Assignments are stored separately from high-frequency telemetry. Queries
return assignments whose observed interval overlaps the requested range.

Retention uses the configured diagnostic horizon and independent profile,
node, and database hard caps. Query truncation is explicit through
`runnerAssignmentsTruncated`. Deleted assignments increment the profile
retention floor; compacted profile provenance increments node and database
incompleteness floors.

No empty result is treated as proof that a job did not run on the node.
Recovered or fixed workers can remain explicitly unattributed.

## Diagnostic scope

Node-history routes return assignments for every retained profile on the
authorized node. Profile-history routes return only that profile's
assignments and omit node-wide runner-assignment loss counts.

Tenant, node, and profile restrictions on `PitCrew-Diagnostics` credentials
remain authoritative. The routes are read-only and cannot mutate workflows,
runners, capacity, managers, Docker, or hosts.
