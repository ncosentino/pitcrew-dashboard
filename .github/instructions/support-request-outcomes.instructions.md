---
applyTo: "src/PitCrew.Support.Agent.App/{AgentReplayCache,SupportAgentRequestProcessor,SupportAgentWorker}.cs,src/PitCrew.Support.Agent.App.Tests/SupportAgentRequestProcessorTests.cs,src/PitCrew.Support.Protocol/SupportRequestRejectionDispositions.cs,src/PitCrew.Support.Protocol.Tests/SupportRequestRejectionDispositionTests.cs"
---

# Durable support request outcomes

- Persist the first closed request outcome before relay reporting. Redelivery
  and restart must reuse it without rerunning diagnostics or replacing it with
  a later replay disposition.
- Bound broker execution below request expiry and reserve time to report the
  terminal outcome. Request-scoped cancellation is a durable timeout; service
  shutdown cancellation is not.
- Route serialized relay-envelope failures through the typed outcome path;
  never treat a malformed dispatched request as an empty successful poll.
