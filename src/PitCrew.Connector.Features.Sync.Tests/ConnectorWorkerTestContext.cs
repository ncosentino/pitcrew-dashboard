using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using PitCrew.Protocol;

namespace PitCrew.Connector.Features.Sync.Tests;

internal sealed class ConnectorWorkerTestContext : IAsyncDisposable
{
  private readonly ConnectorHttpMessageHandler _handler;
  private readonly Mock<IHttpClientFactory> _httpClientFactory;

  public ConnectorWorkerTestContext()
  {
    Root = Path.Combine(Path.GetTempPath(), $"connector-worker-{Guid.NewGuid():N}");
    Directory.CreateDirectory(Root);
    Options = ConnectorTestData.CreateOptions(Root, Path.Combine(Root, "identity.json"));
    Options.EnrollmentCode = string.Empty;
    Clock = new ConnectorWorkerTestTimeProvider(
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    Mocks = new MockRepository(MockBehavior.Strict);
    ProcessLifetime = Mocks.Create<IConnectorProcessLifetime>();
    var processRunner = Mocks.Create<ISetupProcessRunner>();
    var environment = Mocks.Create<IHostExecutionEnvironment>();
    _httpClientFactory = Mocks.Create<IHttpClientFactory>();
    _handler = new ConnectorHttpMessageHandler(SendAsync);
    var settings = Microsoft.Extensions.Options.Options.Create(Options);
    IdentityStore = new ConnectorIdentityStore(settings);
    ApiClient = new ConnectorApiClient(_httpClientFactory.Object, settings);
    Journal = new ConnectorHealthJournal(
        settings, Clock, NullLogger<ConnectorHealthJournal>.Instance);
    Worker = new ConnectorWorker(
        IdentityStore,
        ApiClient,
        new ObservedStateReader(
            settings, NullLogger<ObservedStateReader>.Instance, Clock),
        ConnectorTestFactory.CreateCapacityExecutor(
            Options,
            ConnectorTestFactory.CreateCapacityResolver(Options),
            processRunner.Object,
            Clock),
        ConnectorTestFactory.CreateRecoveryExecutor(
            Options, processRunner.Object, environment.Object, Clock),
        ConnectorTestFactory.CreateImageRolloutExecutor(
            Options, processRunner.Object, environment.Object, Clock),
        Journal,
        new ConnectorHealthReplayStore(
            settings, Clock, NullLogger<ConnectorHealthReplayStore>.Instance),
        ProcessLifetime.Object,
        settings,
        Clock,
        NullLogger<ConnectorWorker>.Instance);
  }

  public string Root { get; }
  public ConnectorOptions Options { get; }
  public ConnectorWorkerTestTimeProvider Clock { get; }
  public MockRepository Mocks { get; }
  public Mock<IConnectorProcessLifetime> ProcessLifetime { get; }
  public ConnectorIdentityStore IdentityStore { get; }
  public ConnectorApiClient ApiClient { get; }
  public ConnectorHealthJournal Journal { get; }
  public ConnectorWorker Worker { get; }
  public List<ConnectorSyncRequest> Requests { get; } = [];
  public Func<int, HttpResponseMessage>? ResponseFactory { get; set; }

  public void EnableHttpTransport() =>
      _httpClientFactory.Setup(value => value.CreateClient("PitCrewConnector"))
          .Returns(() => new HttpClient(_handler, disposeHandler: false));

  public async Task InitializeAsync(CancellationToken cancellationToken)
  {
    await IdentityStore.SaveAsync(
        new ConnectorIdentity("synthetic-connector", Guid.NewGuid(), "synthetic-credential"),
        cancellationToken);
    var profile = Directory.CreateDirectory(Path.Combine(Root, "default"));
    await File.WriteAllTextAsync(
        Path.Combine(profile.FullName, "static-profile.json"), "{}", cancellationToken);
    await File.WriteAllTextAsync(
        Path.Combine(profile.FullName, "observed-state.json"),
        JsonSerializer.Serialize(
            ConnectorTestData.CreateObservedState("default", Clock.GetUtcNow()),
            PitCrewProtocolJsonContext.Default.ManagerObservedState),
        cancellationToken);
  }

  public async Task<ConnectorHealthSnapshot> ReadSnapshotAsync(
      CancellationToken cancellationToken) =>
      JsonSerializer.Deserialize(
          await File.ReadAllTextAsync(
              Path.Combine(Root, "health", "connector-health.json"),
              cancellationToken),
          ConnectorHealthJsonContext.Default.ConnectorHealthSnapshot) ??
      throw new InvalidOperationException("The test health snapshot is missing.");

  public async ValueTask DisposeAsync()
  {
    try
    {
      await Worker.StopAsync(CancellationToken.None);
      Mocks.VerifyAll();
    }
    finally
    {
      Worker.Dispose();
      _handler.Dispose();
      Directory.Delete(Root, recursive: true);
    }
  }

  private async Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request,
      CancellationToken cancellationToken)
  {
    var sync = await request.Content!.ReadFromJsonAsync(
        PitCrewProtocolJsonContext.Default.ConnectorSyncRequest, cancellationToken) ??
        throw new InvalidOperationException("The test request was empty.");
    Requests.Add(sync);
    return ResponseFactory?.Invoke(Requests.Count) ??
        new HttpResponseMessage(HttpStatusCode.OK)
        {
          Content = JsonContent.Create(
              new ConnectorSyncResponse(Clock.GetUtcNow(), 15, null, null),
              PitCrewProtocolJsonContext.Default.ConnectorSyncResponse),
        };
  }
}
