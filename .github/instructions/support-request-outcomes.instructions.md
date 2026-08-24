---
applyTo: "src/PitCrew.Support.Agent.App/{AgentReplayCache,SupportAgentRequestProcessor,SupportAgentWorker}.cs,src/PitCrew.Support.Agent.App.Tests/SupportAgentRequestProcessorTests.cs,src/PitCrew.Support.Protocol/SupportRequestRejectionDispositions.cs,src/PitCrew.Support.Protocol.Tests/SupportRequestRejectionDispositionTests.cs"
---

# Durable support request outcomes

- Persist the first closed request outcome before relay reporting. Redelivery
  and restart must reuse it without rerunning diagnostics or replacing it with
  a later replay disposition.
