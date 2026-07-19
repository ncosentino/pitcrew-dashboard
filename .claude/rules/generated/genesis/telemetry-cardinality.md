---
# AUTO-GENERATED from .github/instructions/genesis/telemetry-cardinality.instructions.md — do not edit
paths:
  - "**/*.cs"
---
# Telemetry Cardinality Rules

- Metric labels and tags must come from bounded vocabularies. Never use raw request, user, tenant,
  entity, or correlation identifiers; URLs; paths; prompts; responses; exception messages; or
  free-form reasons as metric dimensions.
- Keep exported span attributes allowlisted. Use bounded discriminators for values intended for
  aggregation instead of narrative values.
- Put precise diagnostic values in structured logs only when operationally necessary and permitted
  by the project's redaction and retention policy. Never record secrets.
