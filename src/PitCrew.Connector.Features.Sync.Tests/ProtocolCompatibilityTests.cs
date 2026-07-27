using System.Text.Json;

using PitCrew.Protocol;

namespace PitCrew.Connector.Features.Sync.Tests;

public sealed class ProtocolCompatibilityTests
{
  [Test]
  public async Task Protocol_Two_Payloads_Remain_Readable_Without_Capacity_Fields()
  {
    var request = JsonSerializer.Deserialize(
        """
        {
          "protocolVersion": 2,
          "connectorVersion": "2.0.0",
          "sentAt": "2026-07-24T12:00:00+00:00",
          "profiles": []
        }
        """,
        PitCrewProtocolJsonContext.Default.ConnectorSyncRequest);
    var response = JsonSerializer.Deserialize(
        """
        {
          "acceptedAt": "2026-07-24T12:00:00+00:00",
          "nextPollSeconds": 15,
          "credentialRotation": null
        }
        """,
        PitCrewProtocolJsonContext.Default.ConnectorSyncResponse);

    await Assert.That(request).IsNotNull();
    await Assert.That(request!.CapacityOperator).IsNull();
    await Assert.That(request.CapacityCommandOutcome).IsNull();
    await Assert.That(response).IsNotNull();
    await Assert.That(response!.CapacityCommand).IsNull();
  }

  [Test]
  public async Task Contract_Ten_Observed_State_Remains_Readable_Without_Contract_Eleven_Fields()
  {
    var profile = JsonSerializer.Deserialize(
        """
        {
          "schemaVersion": 1,
          "managerContractVersion": 10,
          "profileId": "default",
          "managerInstanceId": "manager-instance",
          "managerStatus": "running",
          "observedAt": "2026-07-26T12:00:00+00:00",
          "scope": "repo",
          "generation": 1,
          "desiredStateHash": null,
          "desiredStateStatus": "accepted",
          "desiredSlots": 1,
          "activeSlots": 1,
          "drainingSlots": 0,
          "eligibleSlots": 1,
          "slots": [
            {
              "key": "repo-example-000001",
              "repository": "https://github.com/example/project",
              "desired": true,
              "processRunning": true,
              "state": "online",
              "failureCount": 0,
              "backoffSeconds": 0,
              "updatedAt": "2026-07-26T12:00:00+00:00",
              "resources": {
                "cpuCores": 1.25,
                "memoryWorkingSetBytes": 1073741824,
                "pids": 48
              },
              "activity": "busy",
              "target": "repo:example/project",
              "registrationStatus": "connected"
            }
          ],
          "resourceTelemetry": null,
          "configuredSlots": null,
          "autoscaling": null
        }
        """,
        PitCrewProtocolJsonContext.Default.ManagerObservedState);

    await Assert.That(profile).IsNotNull();
    await Assert.That(profile!.ResourcePolicy).IsNull();
    await Assert.That(profile.Slots).HasSingleItem();
    await Assert.That(profile.Slots[0].ImageId).IsNull();
    await Assert.That(profile.Slots[0].LastExit).IsNull();
    await Assert.That(profile.Slots[0].Resources?.NetworkRxBytes).IsNull();
    await Assert.That(profile.Slots[0].Resources?.BlockWriteBytes).IsNull();
  }

  [Test]
  public async Task Contract_Eleven_Observed_State_Round_Trips_Through_Json()
  {
    var profile = JsonSerializer.Deserialize(
        """
        {
          "schemaVersion": 1,
          "managerContractVersion": 11,
          "profileId": "default",
          "managerInstanceId": "manager-instance",
          "managerStatus": "running",
          "observedAt": "2026-07-26T12:00:00+00:00",
          "scope": "repo",
          "generation": 1,
          "desiredStateHash": null,
          "desiredStateStatus": "accepted",
          "desiredSlots": 1,
          "activeSlots": 1,
          "drainingSlots": 0,
          "eligibleSlots": 1,
          "resourcePolicy": {
            "memoryBytes": 8589934592,
            "memorySwapBytes": 10737418240,
            "cpuCores": "2.5",
            "pids": 1024
          },
          "slots": [
            {
              "key": "repo-example-000001",
              "repository": "https://github.com/example/project",
              "desired": true,
              "processRunning": true,
              "state": "online",
              "failureCount": 0,
              "backoffSeconds": 0,
              "updatedAt": "2026-07-26T12:00:00+00:00",
              "resources": {
                "cpuCores": 1.25,
                "memoryWorkingSetBytes": 1073741824,
                "pids": 48,
                "networkRxBytes": 1048576,
                "networkTxBytes": 0,
                "blockReadBytes": 536870912,
                "blockWriteBytes": null
              },
              "activity": "busy",
              "target": "repo:example/project",
              "registrationStatus": "connected",
              "imageId": "sha256:1111111111111111111111111111111111111111111111111111111111111111",
              "lastExit": {
                "observedAt": "2026-07-26T11:55:00+00:00",
                "classification": "oom-killed",
                "exitCode": 137,
                "signal": 9,
                "dockerOomKilled": true,
                "evidence": "docker-inspect"
              }
            }
          ],
          "resourceTelemetry": null,
          "configuredSlots": 8,
          "autoscaling": {
            "mode": "scale-set",
            "status": "running",
            "minimumIdleSlots": 0,
            "maximumSlots": 8,
            "targetSlots": 1,
            "assignedJobs": 1,
            "runningJobs": 1,
            "availableJobs": 0,
            "idleRunners": 0,
            "busyRunners": 1,
            "scaleDownDelaySeconds": 120,
            "scaleSetCount": 1,
            "scaleDownAt": null,
            "lastError": null,
            "maximumActiveWorkers": 6,
            "targets": [
              {
                "key": "repo:example/project",
                "repository": "https://github.com/example/project",
                "maximumSlots": 8,
                "targetSlots": 1,
                "localActiveWorkers": 1,
                "localIdleWorkers": 0,
                "localBusyWorkers": 1,
                "localDrainingWorkers": 0,
                "statistics": {
                  "observedAt": "2026-07-26T11:59:00+00:00",
                  "availableJobs": 0,
                  "acquiredJobs": 0,
                  "assignedJobs": 1,
                  "runningJobs": 1,
                  "registeredRunners": 8,
                  "busyRunners": 1,
                  "idleRunners": 7
                }
              }
            ]
          }
        }
        """,
        PitCrewProtocolJsonContext.Default.ManagerObservedState);

    await Assert.That(profile).IsNotNull();
    var reserialized = JsonSerializer.Deserialize(
        JsonSerializer.Serialize(
            profile!,
            PitCrewProtocolJsonContext.Default.ManagerObservedState),
        PitCrewProtocolJsonContext.Default.ManagerObservedState);

    await Assert.That(reserialized).IsNotNull();
    await Assert.That(reserialized!.ResourcePolicy)
        .IsEqualTo(profile!.ResourcePolicy);
    await Assert.That(reserialized.ResourcePolicy?.CpuCores)
        .IsEqualTo("2.5");
    await Assert.That(reserialized.Autoscaling?.MaximumActiveWorkers)
        .IsEqualTo(6);
    await Assert.That(reserialized.Autoscaling?.Targets)
        .IsNotNull();
    await Assert.That(reserialized.Autoscaling!.Targets!)
        .HasSingleItem();
    await Assert.That(reserialized.Autoscaling.Targets![0].Statistics)
        .IsEqualTo(profile.Autoscaling?.Targets?[0].Statistics);
    await Assert.That(reserialized.Slots).HasSingleItem();
    await Assert.That(reserialized.Slots[0].LastExit)
        .IsEqualTo(profile.Slots[0].LastExit);
    await Assert.That(reserialized.Slots[0].ImageId)
        .IsEqualTo(profile.Slots[0].ImageId);
    await Assert.That(reserialized.Slots[0].Resources?.NetworkTxBytes)
        .IsEqualTo(0);
    await Assert.That(reserialized.Slots[0].Resources?.BlockWriteBytes)
        .IsNull();
  }

  [Test]
  public async Task Protocol_Three_Payloads_Remain_Readable_Without_Recovery_Fields()
  {
    var request = JsonSerializer.Deserialize(
        """
        {
          "protocolVersion": 3,
          "connectorVersion": "3.0.0",
          "sentAt": "2026-07-26T12:00:00+00:00",
          "profiles": []
        }
        """,
        PitCrewProtocolJsonContext.Default.ConnectorSyncRequest);
    var response = JsonSerializer.Deserialize(
        """
        {
          "acceptedAt": "2026-07-26T12:00:00+00:00",
          "nextPollSeconds": 15,
          "credentialRotation": null
        }
        """,
        PitCrewProtocolJsonContext.Default.ConnectorSyncResponse);

    await Assert.That(request).IsNotNull();
    await Assert.That(request!.RecoveryOperator).IsNull();
    await Assert.That(request.RecoveryCommandProgress).IsNull();
    await Assert.That(request.RecoveryCommandOutcome).IsNull();
    await Assert.That(response).IsNotNull();
    await Assert.That(response!.RecoveryCommand).IsNull();
  }

  [Test]
  public async Task Recovery_Command_Round_Trips_On_Protocol_Four()
  {
    var response = JsonSerializer.Deserialize(
        """
        {
          "acceptedAt": "2026-07-27T12:00:00+00:00",
          "nextPollSeconds": 15,
          "credentialRotation": null,
          "recoveryCommand": {
            "commandId": "8a1d3d4e-2f0c-4d64-9b0e-6f2b8f0f8a11",
            "profileId": "default",
            "expectedManagerInstanceId": "manager-1",
            "expectedGeneration": 7,
            "expectedDesiredStateHash": null,
            "requestedAt": "2026-07-27T12:00:00+00:00",
            "expiresAt": "2026-07-27T12:05:00+00:00"
          }
        }
        """,
        PitCrewProtocolJsonContext.Default.ConnectorSyncResponse);

    await Assert.That(response).IsNotNull();
    await Assert.That(response!.RecoveryCommand).IsNotNull();
    await Assert.That(response.RecoveryCommand!.ProfileId).IsEqualTo("default");
    await Assert.That(response.RecoveryCommand.ExpectedManagerInstanceId)
        .IsEqualTo("manager-1");
    await Assert.That(response.RecoveryCommand.ExpectedGeneration).IsEqualTo(7);
    await Assert.That(response.RecoveryCommand.ExpectedDesiredStateHash)
        .IsNull();
  }
}
