using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Mvc.Testing;

using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Protocol;

namespace PitCrew.Dashboard.WebApi.Tests;

[NotInParallel]
public sealed class HostingTests
{
  [Test]
  public async Task Connector_Enrolls_Synchronizes_And_Appears_In_Fleet(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(
          databasePath);
      await using var factory = new WebApplicationFactory<Program>();
      using var client = factory.CreateClient();
      var identity = await DashboardTestHelpers.EnrollAsync(
          client,
          "connector-instance",
          "Build Server",
          DashboardTestHelpers.EnrollmentToken,
          cancellationToken);
      await DashboardTestHelpers.SynchronizeAsync(
          client,
          identity,
          "1.0.0",
          DashboardTestHelpers.CreateObservedState(
              "default",
              "https://github.com/example/project"),
          cancellationToken);

      var fleet = await client.GetFromJsonAsync<FleetResponse>(
          "/api/fleet/v1/nodes",
          cancellationToken);

      await Assert.That(fleet).IsNotNull();
      await Assert.That(fleet!.Nodes).HasSingleItem();
      await Assert.That(fleet.Nodes[0].DisplayName)
          .IsEqualTo("Build Server");
      await Assert.That(fleet.Nodes[0].Profiles).HasSingleItem();
      await Assert.That(fleet.Nodes[0].Profiles[0].Slots).HasSingleItem();
      await Assert.That(fleet.Nodes[0].Profiles[0].Slots[0].State)
          .IsEqualTo("online");
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Multiple_Connectors_Isolate_Overlapping_Profile_Names(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(
          databasePath);
      await using var factory = new WebApplicationFactory<Program>();
      using var client = factory.CreateClient();
      var first = await DashboardTestHelpers.EnrollAsync(
          client,
          "connector-one",
          "Server One",
          DashboardTestHelpers.EnrollmentToken,
          cancellationToken);
      var second = await DashboardTestHelpers.EnrollAsync(
          client,
          "connector-two",
          "Server Two",
          DashboardTestHelpers.EnrollmentToken,
          cancellationToken);
      await DashboardTestHelpers.SynchronizeAsync(
          client,
          first,
          "1.0.0",
          DashboardTestHelpers.CreateObservedState(
              "default",
              "https://github.com/example/one"),
          cancellationToken);
      await DashboardTestHelpers.SynchronizeAsync(
          client,
          second,
          "1.0.0",
          DashboardTestHelpers.CreateObservedState(
              "default",
              "https://github.com/example/two"),
          cancellationToken);

      var fleet = await client.GetFromJsonAsync<FleetResponse>(
          "/api/fleet/v1/nodes",
          cancellationToken);

      await Assert.That(fleet).IsNotNull();
      await Assert.That(fleet!.Nodes).Count().IsEqualTo(2);
      var firstNode = fleet.Nodes.Single(
          node => node.DisplayName == "Server One");
      var secondNode = fleet.Nodes.Single(
          node => node.DisplayName == "Server Two");
      await Assert.That(firstNode.Profiles[0].ProfileId)
          .IsEqualTo("default");
      await Assert.That(secondNode.Profiles[0].ProfileId)
          .IsEqualTo("default");
      await Assert.That(firstNode.Profiles[0].Slots[0].Repository)
          .IsEqualTo("https://github.com/example/one");
      await Assert.That(secondNode.Profiles[0].Slots[0].Repository)
          .IsEqualTo("https://github.com/example/two");
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Connector_Enrollment_Rejects_Invalid_Token(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(
          databasePath);
      await using var factory = new WebApplicationFactory<Program>();
      using var client = factory.CreateClient();
      using var enrollment = new HttpRequestMessage(
          HttpMethod.Post,
          "/api/connectors/v1/enroll")
      {
        Content = JsonContent.Create(new ConnectorEnrollmentRequest(
              "connector-instance",
              "Build Server")),
      };
      enrollment.Headers.Add(
          "X-PitCrew-Enrollment-Token",
          "incorrect-token");

      using var response = await client.SendAsync(
          enrollment,
          cancellationToken);

      await Assert.That(response.StatusCode)
          .IsEqualTo(HttpStatusCode.Unauthorized);
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }
}
