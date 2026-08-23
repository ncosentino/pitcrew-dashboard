---
applyTo: "src/PitCrew.Support.Canary.*/**,scripts/canary/**,.github/workflows/support-canary.yml,docs/testing/support-canary.md,docs/adr/adr-0010-extensible-support-canary-harness.md,src/PitCrew.Support.Agent.App/{SupportAgentRequestProcessor,SupportAgentSettingsFinalizer,SupportAgentStartupStatusWriter,SupportAgentWorker}.cs,src/PitCrew.Support.Agent.App.Tests/{SupportAgentRequestProcessor,SupportAgentSettingsFinalizer,SupportAgentStartupStatusWriter}Tests.cs"
---

# Cross-repository support canary

- Run actual candidate binaries from clean, full-SHA PitCrew and Dashboard
  checkouts. Never replace protocol, collector, verifier, agent, broker, relay,
  or Dashboard behavior with lookalike fixtures.
- Keep source resolution, build, scaffold, AppHost lifecycle, scenario
  execution, evidence, and teardown independently invocable.
- Keep AppHost scenario-agnostic. Add scenarios through `ICanaryScenario`,
  explicit registration, and declared runtime capabilities.
- Keep plan, runtime, and scenario-result schemas versioned, bounded, strict,
  and free of credentials, payloads, reports, exception text, private host
  details, and developer paths.
- Pass ephemeral secrets only through child-process environment. Never place
  them in command arguments, logs, manifests, uploaded artifacts, or source.
- Bound every wait and cleanup. Stop only the exact run ID and PID/start-time
  fence; never clean by process, service, project, or container name.
- The portable profile proves process, protocol, cryptography, and file-only
  compatibility only. Do not claim installed service, ACL, firewall, systemd,
  container, or physical-host evidence.
- Use the same scenario implementation from the external runner and
  `Aspire.Hosting.Testing`; do not duplicate scenario steps in tests or
  workflows.
- Run untrusted pull requests only on public GitHub-hosted capacity with no
  production secrets and no `pull_request_target`.
- Preserve bounded request-processing dispositions and explicit accepted-poll
  evidence. A rejected request must never look like a successful idle poll.

See [ADR-0010](../../docs/adr/adr-0010-extensible-support-canary-harness.md)
and [Support canary](../../docs/testing/support-canary.md).
