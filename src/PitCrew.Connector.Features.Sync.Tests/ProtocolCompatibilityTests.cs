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
    await Assert.That(profile.Update).IsNull();
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
          },
          "update": {
            "status": "rolling",
            "targetImage": "ghcr.io/example/runner@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "targetImageId": "sha256:2222222222222222222222222222222222222222222222222222222222222222",
            "targetRevision": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            "currentWorkers": 0,
            "staleWorkers": 1,
            "lastError": null
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
    await Assert.That(reserialized.Update).IsEqualTo(profile.Update);
    await Assert.That(reserialized.Update?.TargetImageId)
        .IsEqualTo("sha256:" + new string('2', 64));
  }

  [Test]
  public async Task Contract_Fourteen_Runner_Hash_Round_Trips_Through_Json()
  {
    var profile = JsonSerializer.Deserialize(
        """
        {
          "schemaVersion": 1,
          "managerContractVersion": 14,
          "profileId": "default",
          "managerInstanceId": "manager-instance",
          "managerStatus": "running",
          "observedAt": "2026-08-04T12:00:00+00:00",
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
              "updatedAt": "2026-08-04T12:00:00+00:00",
              "resources": null,
              "activity": "busy",
              "target": "repo:example/project",
              "registrationStatus": "connected",
              "imageId": null,
              "lastExit": null,
              "runnerNameHash": "e0054523055d4ebd049b2b33a1f3b55ba66e5f194b1bbbe5a69eca1ac6a5bf41"
            }
          ],
          "resourceTelemetry": null,
          "configuredSlots": 1,
          "autoscaling": null
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
    await Assert.That(reserialized!.Slots).HasSingleItem();
    await Assert.That(reserialized.Slots[0].RunnerNameHash)
        .IsEqualTo(
            "e0054523055d4ebd049b2b33a1f3b55ba66e5f194b1bbbe5a69eca1ac6a5bf41");
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
  public async Task Contract_Twelve_Observed_State_Round_Trips_Through_Json()
  {
    var profile = JsonSerializer.Deserialize(
        """
        {
          "schemaVersion": 1,
          "managerContractVersion": 12,
          "profileId": "default",
          "managerInstanceId": "manager-instance",
          "managerStatus": "running",
          "observedAt": "2026-07-26T12:00:00+00:00",
          "scope": "repo",
          "generation": 1,
          "desiredStateHash": null,
          "desiredStateStatus": "accepted",
          "desiredSlots": 1,
          "activeSlots": 0,
          "drainingSlots": 0,
          "eligibleSlots": 0,
          "resourcePolicy": null,
          "slots": [],
          "resourceTelemetry": null,
          "configuredSlots": 1,
          "autoscaling": null,
          "operationJournal": {
            "status": "truncated",
            "capacity": 32,
            "highestSequence": 41,
            "droppedEvents": 9,
            "events": [
              {
                "sequence": 40,
                "managerInstanceId": "manager-instance-0",
                "observedAt": "2026-07-26T11:58:00+00:00",
                "subsystem": "recovery",
                "operation": "manager-shutdown",
                "target": null,
                "outcome": "succeeded",
                "durationMilliseconds": 0,
                "attempt": null,
                "consecutiveFailures": null,
                "retryAt": null,
                "reason": "none",
                "evidence": null
              },
              {
                "sequence": 41,
                "managerInstanceId": "manager-instance",
                "observedAt": "2026-07-26T11:59:30+00:00",
                "subsystem": "docker",
                "operation": "docker-run",
                "target": "repo-example-000001",
                "outcome": "retry-scheduled",
                "durationMilliseconds": 1200,
                "attempt": 3,
                "consecutiveFailures": 2,
                "retryAt": "2026-07-26T12:00:30+00:00",
                "reason": "docker-failed",
                "evidence": "Docker refused to start the worker container."
              }
            ]
          },
          "subsystemHealth": {
            "docker": {
              "state": "degraded",
              "observedAt": "2026-07-26T12:00:00+00:00",
              "consecutiveFailures": 2,
              "retryAt": "2026-07-26T12:00:30+00:00",
              "lastSuccess": {
                "operation": "docker-ping",
                "observedAt": "2026-07-26T11:55:00+00:00",
                "durationMilliseconds": 4,
                "reason": "none",
                "evidence": null
              },
              "lastFailure": {
                "operation": "docker-run",
                "observedAt": "2026-07-26T11:59:30+00:00",
                "durationMilliseconds": 1200,
                "reason": "docker-failed",
                "evidence": "Docker refused to start the worker container."
              }
            },
            "github": {
              "state": "unknown",
              "observedAt": "2026-07-26T12:00:00+00:00",
              "consecutiveFailures": 0,
              "retryAt": null,
              "lastSuccess": null,
              "lastFailure": null
            }
          },
          "capacityEvidence": {
            "fixed": {
              "observedAt": "2026-07-26T12:00:00+00:00",
              "freshness": "current",
              "targetSlots": 1,
              "activeWorkers": 0,
              "startingWorkers": 0,
              "drainingWorkers": 0,
              "cleanupPendingWorkers": 0,
              "eligibleWorkers": null,
              "localDeficit": 1,
              "eligibilityDeficit": null,
              "reason": "docker-failed",
              "evidence": "Docker refused to start the worker container."
            },
            "targets": []
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
    await Assert.That(reserialized!.OperationJournal).IsNotNull();
    await Assert.That(reserialized.OperationJournal!.Status)
        .IsEqualTo("truncated");
    await Assert.That(reserialized.OperationJournal.DroppedEvents)
        .IsEqualTo(9);
    await Assert.That(reserialized.OperationJournal.Events.Count)
        .IsEqualTo(2);
    await Assert.That(reserialized.OperationJournal.Events[0].DurationMilliseconds)
        .IsEqualTo(0);
    await Assert.That(reserialized.OperationJournal.Events[1].RetryAt)
        .IsNotNull();
    await Assert.That(reserialized.OperationJournal.Events[1])
        .IsEqualTo(profile!.OperationJournal!.Events[1]);
    await Assert.That(reserialized.SubsystemHealth!.Docker)
        .IsEqualTo(profile.SubsystemHealth!.Docker);
    await Assert.That(reserialized.SubsystemHealth.Github.State)
        .IsEqualTo("unknown");
    await Assert.That(reserialized.CapacityEvidence!.Fixed)
        .IsEqualTo(profile.CapacityEvidence!.Fixed);
    await Assert.That(reserialized.CapacityEvidence.Fixed!.EligibleWorkers)
        .IsNull();
    await Assert.That(reserialized.CapacityEvidence.Targets).IsEmpty();
  }

  [Test]
  public async Task Contract_Twelve_Target_Evidence_Round_Trips_Through_Json()
  {
    var evidence = new TargetCapacityDeficitEvidence(
        "repo:example/project",
        "https://github.com/example/project",
        new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.Zero),
        "stale",
        2,
        1,
        0,
        0,
        0,
        0,
        1,
        2,
        "listener-unavailable",
        "The scale-set listener is unavailable.");

    var reserialized = JsonSerializer.Deserialize(
        JsonSerializer.Serialize(
            evidence,
            PitCrewProtocolJsonContext.Default.TargetCapacityDeficitEvidence),
        PitCrewProtocolJsonContext.Default.TargetCapacityDeficitEvidence);

    await Assert.That(reserialized).IsEqualTo(evidence);
  }

  [Test]
  public async Task Contract_Eleven_Observed_State_Remains_Readable_Without_Contract_Twelve_Fields()
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
          "desiredSlots": 0,
          "activeSlots": 0,
          "drainingSlots": 0,
          "eligibleSlots": 0,
          "resourcePolicy": null,
          "slots": []
        }
        """,
        PitCrewProtocolJsonContext.Default.ManagerObservedState);

    await Assert.That(profile).IsNotNull();
    await Assert.That(profile!.OperationJournal).IsNull();
    await Assert.That(profile.SubsystemHealth).IsNull();
    await Assert.That(profile.CapacityEvidence).IsNull();
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

  [Test]
  public async Task Protocol_Nine_Remains_Readable_Without_Connector_Health()
  {
    var request = JsonSerializer.Deserialize(
        """
        {
          "protocolVersion": 9,
          "connectorVersion": "9.0.0",
          "sentAt": "2026-08-07T12:00:00+00:00",
          "profiles": [],
          "capacityOperator": null,
          "capacityCommandOutcome": null,
          "recoveryOperator": null,
          "recoveryCommandProgress": null,
          "recoveryCommandOutcome": null
        }
        """,
        PitCrewProtocolJsonContext.Default.ConnectorSyncRequest);
    var response = JsonSerializer.Deserialize(
        """
        {
          "acceptedAt": "2026-08-07T12:00:01+00:00",
          "nextPollSeconds": 15,
          "credentialRotation": null,
          "capacityCommand": null,
          "recoveryCommand": null
        }
        """,
        PitCrewProtocolJsonContext.Default.ConnectorSyncResponse);

    await Assert.That(request).IsNotNull();
    await Assert.That(request!.ConnectorHealth).IsNull();
    await Assert.That(response).IsNotNull();
    await Assert.That(response!.ConnectorHealthAcknowledgement).IsNull();
  }

  [Test]
  public async Task Connector_Health_Replay_Round_Trips_On_Protocol_Ten()
  {
    var outageId = new Guid(
        "11111111-1111-1111-1111-111111111111");
    var eventId = new Guid(
        "22222222-2222-2222-2222-222222222222");
    var request = new ConnectorSyncRequest(
        10,
        "10.0.0",
        new DateTimeOffset(
            2026,
            8,
            7,
            12,
            0,
            0,
            TimeSpan.Zero),
        [],
        null,
        null,
        null,
        null,
        null,
        new ConnectorHealthReplay(
            new ConnectorHealthReplaySnapshot(
                "degraded",
                new DateTimeOffset(
                    2026,
                    8,
                    7,
                    11,
                    0,
                    0,
                    TimeSpan.Zero),
                new DateTimeOffset(
                    2026,
                    8,
                    7,
                    12,
                    0,
                    0,
                    TimeSpan.Zero),
                new DateTimeOffset(
                    2026,
                    8,
                    7,
                    12,
                    0,
                    0,
                    TimeSpan.Zero),
                null,
                outageId,
                new DateTimeOffset(
                    2026,
                    8,
                    7,
                    11,
                    55,
                    0,
                    TimeSpan.Zero),
                new DateTimeOffset(
                    2026,
                    8,
                    7,
                    12,
                    0,
                    0,
                    TimeSpan.Zero),
                "synchronization-network",
                null,
                "Connector synchronization could not reach Dashboard.",
                3,
                new DateTimeOffset(
                    2026,
                    8,
                    7,
                    12,
                    5,
                    0,
                    TimeSpan.Zero),
                null,
                null,
                null,
                null),
            [
                new ConnectorHealthReplayEvent(
                    eventId,
                    "synchronization-failed",
                    new DateTimeOffset(
                        2026,
                        8,
                        7,
                        12,
                        0,
                        0,
                        TimeSpan.Zero),
                    "degraded",
                    outageId,
                    new DateTimeOffset(
                        2026,
                        8,
                        7,
                        11,
                        55,
                        0,
                        TimeSpan.Zero),
                    "synchronization-network",
                    null,
                    3,
                    300,
                    "Connector synchronization could not reach Dashboard."),
            ]));
    var serialized = JsonSerializer.Serialize(
        request,
        PitCrewProtocolJsonContext.Default.ConnectorSyncRequest);
    var roundTripped = JsonSerializer.Deserialize(
        serialized,
        PitCrewProtocolJsonContext.Default.ConnectorSyncRequest);
    var response = new ConnectorSyncResponse(
        request.SentAt.AddSeconds(1),
        15,
        null,
        null,
        null,
        new ConnectorHealthAcknowledgement([eventId]));
    var responseJson = JsonSerializer.Serialize(
        response,
        PitCrewProtocolJsonContext.Default.ConnectorSyncResponse);
    var responseRoundTrip = JsonSerializer.Deserialize(
        responseJson,
        PitCrewProtocolJsonContext.Default.ConnectorSyncResponse);

    await Assert.That(roundTripped).IsNotNull();
    await Assert.That(roundTripped!.ConnectorHealth).IsNotNull();
    await Assert.That(roundTripped.ConnectorHealth!.Events)
        .HasSingleItem();
    await Assert.That(
            roundTripped.ConnectorHealth.Events[0].EventId)
        .IsEqualTo(eventId);
    await Assert.That(responseRoundTrip).IsNotNull();
    await Assert.That(
            responseRoundTrip!.ConnectorHealthAcknowledgement!.EventIds)
        .HasSingleItem();
    await Assert.That(
            responseRoundTrip.ConnectorHealthAcknowledgement.EventIds[0])
        .IsEqualTo(eventId);
  }
}
