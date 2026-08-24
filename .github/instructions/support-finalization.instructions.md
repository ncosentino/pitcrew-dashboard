---
applyTo: "scripts/Install-PitCrewSupportPlane.ps1,src/PitCrew.Support.Agent.App/{Program,SupportAgentSettingsFinalizer,SupportEnrollmentFinalizationRequestWorker}.cs,src/PitCrew.Support.Agent.App.Tests/*Finalization*Tests.cs,tests/Test-SupportPlaneInstaller*.ps1,docs/support-plane.md"
---

# Installed enrollment finalization

- Finalize only through the typed installer action under the agent service
  identity. Require Active identity and a second accepted poll, preserve exact
  settings through a protected agent-owned backup, keep the broker unchanged,
  and restore ACL/ownership plus service state on failure.
