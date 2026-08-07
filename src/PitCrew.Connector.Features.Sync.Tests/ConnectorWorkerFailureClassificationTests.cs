using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;

namespace PitCrew.Connector.Features.Sync.Tests;

public sealed class ConnectorWorkerFailureClassificationTests
{
  [Test]
  public async Task ClassifyHttpFailure_Distinguishes_Transport_Categories()
  {
    var syncNetwork = ConnectorWorker.ClassifyHttpFailure(
        new HttpRequestException("secret network detail"),
        enrollment: false);
    var enrollmentNetwork = ConnectorWorker.ClassifyHttpFailure(
        new HttpRequestException("secret enrollment detail"),
        enrollment: true);
    var dnsFailure = ConnectorWorker.ClassifyHttpFailure(
        new HttpRequestException(
            "secret DNS detail",
            new SocketException(
                (int)SocketError.HostNotFound)),
        enrollment: false);
    var tlsFailure = ConnectorWorker.ClassifyHttpFailure(
        new HttpRequestException(
            "secret TLS detail",
            new AuthenticationException(
                "secret certificate detail")),
        enrollment: false);
    var syncRateLimit = ConnectorWorker.ClassifyHttpFailure(
        new HttpRequestException(
            "secret rate detail",
            null,
            HttpStatusCode.TooManyRequests),
        enrollment: false);
    var enrollmentRateLimit = ConnectorWorker.ClassifyHttpFailure(
        new HttpRequestException(
            "secret rate detail",
            null,
            HttpStatusCode.TooManyRequests),
        enrollment: true);
    var syncServer = ConnectorWorker.ClassifyHttpFailure(
        new HttpRequestException(
            "secret server detail",
            null,
            HttpStatusCode.ServiceUnavailable),
        enrollment: false);
    var enrollmentServer = ConnectorWorker.ClassifyHttpFailure(
        new HttpRequestException(
            "secret server detail",
            null,
            HttpStatusCode.ServiceUnavailable),
        enrollment: true);
    var syncTimeout = ConnectorWorker.ClassifyTimeout(
        enrollment: false);
    var enrollmentTimeout = ConnectorWorker.ClassifyTimeout(
        enrollment: true);

    await Assert.That(syncNetwork.Category)
        .IsEqualTo(
            ConnectorHealthFailureCategories.SynchronizationNetwork);
    await Assert.That(enrollmentNetwork.Category)
        .IsEqualTo(
            ConnectorHealthFailureCategories.EnrollmentNetwork);
    await Assert.That(dnsFailure.Category)
        .IsEqualTo(
            ConnectorHealthFailureCategories.SynchronizationNetwork);
    await Assert.That(tlsFailure.Category)
        .IsEqualTo(
            ConnectorHealthFailureCategories.SynchronizationNetwork);
    await Assert.That(syncRateLimit.Category)
        .IsEqualTo(
            ConnectorHealthFailureCategories.SynchronizationRateLimited);
    await Assert.That(enrollmentRateLimit.Category)
        .IsEqualTo(
            ConnectorHealthFailureCategories.EnrollmentRateLimited);
    await Assert.That(syncServer.Category)
        .IsEqualTo(
            ConnectorHealthFailureCategories.SynchronizationServer);
    await Assert.That(enrollmentServer.Category)
        .IsEqualTo(
            ConnectorHealthFailureCategories.EnrollmentServer);
    await Assert.That(syncTimeout.Category)
        .IsEqualTo(
            ConnectorHealthFailureCategories.SynchronizationTimeout);
    await Assert.That(enrollmentTimeout.Category)
        .IsEqualTo(
            ConnectorHealthFailureCategories.EnrollmentTimeout);
    await Assert.That(
            new[]
            {
              syncNetwork.Detail,
              enrollmentNetwork.Detail,
              dnsFailure.Detail,
              tlsFailure.Detail,
              syncRateLimit.Detail,
              enrollmentRateLimit.Detail,
              syncServer.Detail,
              enrollmentServer.Detail,
              syncTimeout.Detail,
              enrollmentTimeout.Detail,
            }.Any(detail => detail.Contains(
                "secret",
                StringComparison.Ordinal)))
        .IsFalse()
        .Because("classified failure details must not retain exception text");
  }
}
