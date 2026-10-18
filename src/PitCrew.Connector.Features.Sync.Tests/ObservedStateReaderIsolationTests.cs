using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using PitCrew.Protocol;

namespace PitCrew.Connector.Features.Sync.Tests;

public sealed class ObservedStateReaderIsolationTests
{
  [Test]
  public async Task Auxiliary_State_Directories_Do_Not_Degrade_Profile_Inventory(
      CancellationToken cancellationToken)
  {
    await using var context = new ConnectorWorkerTestContext();
    await context.InitializeAsync(cancellationToken);
    Directory.CreateDirectory(Path.Combine(context.Root, "host-admission", "example"));
    var service = Directory.CreateDirectory(Path.Combine(context.Root, "image-builder-service"));
    await File.WriteAllTextAsync(
        Path.Combine(service.FullName, "service.json"), "{}", cancellationToken);
    var reader = CreateReader(context);

    var result = await reader.ReadAsync(cancellationToken);

    await Assert.That(result.IsComplete).IsTrue();
    await Assert.That(result.Profiles).HasSingleItem();
    await Assert.That(result.Inventory?.Coverage).IsEqualTo("complete");
    await Assert.That(result.Failure).IsNull();
  }

  [Test]
  public async Task Semantically_Invalid_State_Is_Isolated_And_Does_Not_Poison_The_Cache(
      CancellationToken cancellationToken)
  {
    await using var context = new ConnectorWorkerTestContext();
    await context.InitializeAsync(cancellationToken);
    var reader = CreateReader(context);
    var initial = await reader.ReadAsync(cancellationToken);
    var path = Path.Combine(context.Root, "default", "observed-state.json");
    var invalid = initial.Profiles[0] with { ActiveSlots = 2, Generation = 2 };
    await Assert.That(ManagerObservedStateValidator.IsValid(invalid)).IsFalse();
    await File.WriteAllTextAsync(
        path,
        JsonSerializer.Serialize(invalid, PitCrewProtocolJsonContext.Default.ManagerObservedState),
        cancellationToken);

    var cached = await reader.ReadAsync(cancellationToken);
    var restarted = await CreateReader(context).ReadAsync(cancellationToken);
    await Assert.That(cached.IsComplete).IsFalse();
    await Assert.That(cached.Inventory?.Coverage).IsEqualTo("partial");
    await Assert.That(cached.Profiles[0]).IsEqualTo(initial.Profiles[0]);
    await Assert.That(cached.Failure?.Category)
        .IsEqualTo(ConnectorHealthFailureCategories.ProfileStateInvalid);
    await Assert.That(restarted.Profiles).IsEmpty();
    await Assert.That(restarted.Inventory?.Coverage).IsEqualTo("unavailable");

    var repaired = initial.Profiles[0] with { Generation = 3 };
    await File.WriteAllTextAsync(
        path,
        JsonSerializer.Serialize(repaired, PitCrewProtocolJsonContext.Default.ManagerObservedState),
        cancellationToken);
    var recovered = await reader.ReadAsync(cancellationToken);
    await Assert.That(recovered.IsComplete).IsTrue();
    await Assert.That(recovered.Profiles[0].Generation).IsEqualTo(3);
    File.Delete(path);
    var missing = await reader.ReadAsync(cancellationToken);
    await Assert.That(missing.IsComplete).IsFalse();
    await Assert.That(missing.Profiles[0].Generation).IsEqualTo(3);
  }

  [Test]
  public async Task Null_Slot_Is_Invalid_Observation_Not_A_Fatal_Reader_Error(
      CancellationToken cancellationToken)
  {
    await using var context = new ConnectorWorkerTestContext();
    await context.InitializeAsync(cancellationToken);
    var path = Path.Combine(context.Root, "default", "observed-state.json");
    var payload = JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken))!.AsObject();
    payload["slots"] = new JsonArray((JsonNode?)null);
    await File.WriteAllTextAsync(path, payload.ToJsonString(), cancellationToken);

    var result = await CreateReader(context).ReadAsync(cancellationToken);

    await Assert.That(result.IsComplete).IsFalse();
    await Assert.That(result.Profiles).IsEmpty();
    await Assert.That(result.Failure?.Category)
        .IsEqualTo(ConnectorHealthFailureCategories.ProfileStateInvalid);
  }

  private static ObservedStateReader CreateReader(ConnectorWorkerTestContext context) =>
      new(
          Options.Create(context.Options),
          NullLogger<ObservedStateReader>.Instance,
          context.Clock);

  [Test]
  [Arguments("[]")]
  [Arguments("null")]
  [Arguments("""{"managerContractVersion":"invalid"}""")]
  [Arguments("""{"managerContractVersion":null}""")]
  public async Task Invalid_Json_Shapes_Are_Isolated_Not_Fatal(
      string payload,
      CancellationToken cancellationToken)
  {
    await using var context = new ConnectorWorkerTestContext();
    await context.InitializeAsync(cancellationToken);
    await File.WriteAllTextAsync(
        Path.Combine(context.Root, "default", "observed-state.json"),
        payload,
        cancellationToken);

    var result = await CreateReader(context).ReadAsync(cancellationToken);

    await Assert.That(result.IsComplete).IsFalse()
        .Because("invalid state must remain explicitly unavailable");
    await Assert.That(result.Profiles).IsEmpty();
    await Assert.That(result.Failure?.Category)
        .IsEqualTo(ConnectorHealthFailureCategories.ProfileStateInvalid);
  }
}
