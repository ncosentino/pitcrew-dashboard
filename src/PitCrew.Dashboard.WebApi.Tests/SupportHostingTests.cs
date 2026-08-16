using System.Globalization;
using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Mvc.Testing;

using PitCrew.Dashboard.Features.Access;
using PitCrew.Dashboard.Features.Support;
using PitCrew.Support.Protocol;

namespace PitCrew.Dashboard.WebApi.Tests;

[NotInParallel]
public sealed class SupportHostingTests
{
  [Test]
  public async Task Diagnostic_Credential_Can_Create_And_Read_Support_Session(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(databasePath);
      await using var factory = new WebApplicationFactory<Program>();
      using var client = factory.CreateClient();
      var session = await DashboardTestHelpers.GetSessionAsync(client, cancellationToken);
      var nodeKeys = SupportKeyFactory.CreateNodeKeys();
      using var enrollmentRequest = new HttpRequestMessage(
          HttpMethod.Post,
          $"/api/tenants/{DashboardTestHelpers.TenantId}/support/v1/enrollments")
      {
        Content = JsonContent.Create(new CreateSupportEnrollmentRequest(
            "Support node",
            nodeKeys.Signing.PublicKeySubjectPublicKeyInfoBase64Url,
            nodeKeys.Encryption.PublicKeySubjectPublicKeyInfoBase64Url)),
      };
      enrollmentRequest.Headers.Add(
          DashboardTestHelpers.AntiforgeryHeader,
          session.AntiforgeryToken);
      using var enrollmentResponse = await client.SendAsync(enrollmentRequest, cancellationToken);
      enrollmentResponse.EnsureSuccessStatusCode();
      var enrollment = await enrollmentResponse.Content.ReadFromJsonAsync<CreatedSupportEnrollmentResponse>(
          cancellationToken) ??
          throw new InvalidOperationException("Support enrollment response was empty.");
      var credential = await DashboardTestHelpers.CreateDiagnosticCredentialAsync(
          client,
          session.AntiforgeryToken,
          DashboardTestHelpers.TenantId,
          "support diagnostic credential",
          DateTimeOffset.Parse("2027-08-01T00:00:00+00:00", CultureInfo.InvariantCulture),
          [],
          [],
          cancellationToken);

      using var createResponse = await DashboardTestHelpers.SendDiagnosticAsync(
          client,
          HttpMethod.Post,
          $"/api/tenants/{DashboardTestHelpers.TenantId}/support/v1/sessions",
          credential.Value,
          new CreateSupportDiagnosticSessionRequest(
              Guid.Parse(enrollment.NodeId, CultureInfo.InvariantCulture),
              SupportDiagnosticModes.ConnectorOffline,
              null,
              300),
          cancellationToken);
      var created = await createResponse.Content.ReadFromJsonAsync<SupportDiagnosticSessionResponse>(
          cancellationToken) ??
          throw new InvalidOperationException("Support session response was empty.");
      using var getResponse = await DashboardTestHelpers.SendDiagnosticAsync(
          client,
          HttpMethod.Get,
          $"/api/tenants/{DashboardTestHelpers.TenantId}/support/v1/sessions/{created.SessionId}",
          credential.Value,
          null,
          cancellationToken);
      var fetched = await getResponse.Content.ReadFromJsonAsync<SupportDiagnosticSessionResponse>(
          cancellationToken) ??
          throw new InvalidOperationException("Support session fetch response was empty.");

      await Assert.That(createResponse.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
      await Assert.That(getResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
      await Assert.That(fetched.Status).IsEqualTo("Queued");
      await Assert.That(fetched.DiagnosticMode).IsEqualTo(SupportDiagnosticModes.ConnectorOffline);
      await Assert.That(fetched.Result).IsNull();
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Browser_Support_Session_Post_Requires_Antiforgery(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(databasePath);
      await using var factory = new WebApplicationFactory<Program>();
      using var client = factory.CreateClient();
      await DashboardTestHelpers.GetSessionAsync(client, cancellationToken);
      using var response = await client.PostAsJsonAsync(
          $"/api/tenants/{DashboardTestHelpers.TenantId}/support/v1/sessions",
          new CreateSupportDiagnosticSessionRequest(
              Guid.Parse("11111111-1111-1111-1111-111111111111", CultureInfo.InvariantCulture),
              SupportDiagnosticModes.Full,
              null,
              300),
          cancellationToken);

      await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Profile_Scoped_Diagnostic_Credential_Cannot_Request_Unscoped_Support_Session(
      CancellationToken cancellationToken)
  {
    var databasePath = DashboardTestHelpers.CreateDatabasePath();
    try
    {
      using var configuration = new TestConfigurationScope(databasePath);
      await using var factory = new WebApplicationFactory<Program>();
      using var client = factory.CreateClient();
      var session = await DashboardTestHelpers.GetSessionAsync(client, cancellationToken);
      var nodeKeys = SupportKeyFactory.CreateNodeKeys();
      using var enrollmentRequest = new HttpRequestMessage(
          HttpMethod.Post,
          $"/api/tenants/{DashboardTestHelpers.TenantId}/support/v1/enrollments")
      {
        Content = JsonContent.Create(new CreateSupportEnrollmentRequest(
            "Support node",
            nodeKeys.Signing.PublicKeySubjectPublicKeyInfoBase64Url,
            nodeKeys.Encryption.PublicKeySubjectPublicKeyInfoBase64Url)),
      };
      enrollmentRequest.Headers.Add(
          DashboardTestHelpers.AntiforgeryHeader,
          session.AntiforgeryToken);
      using var enrollmentResponse = await client.SendAsync(enrollmentRequest, cancellationToken);
      enrollmentResponse.EnsureSuccessStatusCode();
      var enrollment = await enrollmentResponse.Content.ReadFromJsonAsync<CreatedSupportEnrollmentResponse>(
          cancellationToken) ??
          throw new InvalidOperationException("Support enrollment response was empty.");
      var credential = await DashboardTestHelpers.CreateDiagnosticCredentialAsync(
          client,
          session.AntiforgeryToken,
          DashboardTestHelpers.TenantId,
          "profile support diagnostic credential",
          DateTimeOffset.Parse("2027-08-01T00:00:00+00:00", CultureInfo.InvariantCulture),
          [],
          ["default"],
          cancellationToken);

      using var createResponse = await DashboardTestHelpers.SendDiagnosticAsync(
          client,
          HttpMethod.Post,
          $"/api/tenants/{DashboardTestHelpers.TenantId}/support/v1/sessions",
          credential.Value,
          new CreateSupportDiagnosticSessionRequest(
              Guid.Parse(enrollment.NodeId, CultureInfo.InvariantCulture),
              SupportDiagnosticModes.Full,
              null,
              300),
          cancellationToken);

      await Assert.That(createResponse.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }
    finally
    {
      DashboardTestHelpers.DeleteDatabase(databasePath);
    }
  }


}

