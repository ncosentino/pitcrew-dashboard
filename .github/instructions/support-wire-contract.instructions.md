---
applyTo: "src/PitCrew.Support.Protocol/**/*Response.cs,src/PitCrew.Support.Protocol.Tests/SupportWireResponseContractTests.cs,src/PitCrew.Support.Agent.App/SupportDashboardIdentityClient.cs,src/PitCrew.Support.Agent.App.Tests/SupportDashboardIdentityClientTests.cs,src/PitCrew.Support.Agent.App.Tests/SupportNodeIdentityStoreTests.cs,src/PitCrew.Dashboard.Features.Support/{SupportApiContracts,SupportCarterModule}.cs,src/PitCrew.Dashboard.WebApi.Tests/*Support*.cs"
---

# Support wire contracts

- Share valid API/agent responses from `PitCrew.Support.Protocol`; forbid
  lookalikes and pin v1 JSON names in API/client/package tests. Breaking changes
  need a new major plus mixed-version test.
