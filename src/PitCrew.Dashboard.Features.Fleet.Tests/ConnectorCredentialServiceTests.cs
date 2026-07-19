namespace PitCrew.Dashboard.Features.Fleet.Tests;

public sealed class ConnectorCredentialServiceTests
{
  [Test]
  public async Task CreateSecrets_Produces_Unique_Typed_Values()
  {
    var service = new ConnectorCredentialService();

    var first = service.CreateNodeCredential();
    var second = service.CreateNodeCredential();
    var enrollmentCode = service.CreateEnrollmentCode();

    await Assert.That(first).IsNotEqualTo(second);
    await Assert.That(first).StartsWith("pc_node_");
    await Assert.That(enrollmentCode).StartsWith("pc_enroll_");
    await Assert.That(service.Hash(first)).IsNotEqualTo(
        service.Hash(second));
  }
}
