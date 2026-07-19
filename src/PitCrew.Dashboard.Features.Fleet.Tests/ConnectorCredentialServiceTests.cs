namespace PitCrew.Dashboard.Features.Fleet.Tests;

public sealed class ConnectorCredentialServiceTests
{
  [Test]
  public async Task CreateCredential_Produces_Unique_Verifiable_Values()
  {
    var service = new ConnectorCredentialService();

    var first = service.CreateCredential();
    var second = service.CreateCredential();

    await Assert.That(first).IsNotEqualTo(second);
    await Assert.That(first.Length).IsGreaterThanOrEqualTo(40);
    await Assert.That(service.Matches(first, first)).IsTrue();
    await Assert.That(service.Matches(first, second)).IsFalse();
  }
}
