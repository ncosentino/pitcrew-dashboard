using System.Net;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace PitCrew.Dashboard.Adapters.GitHub.Tests;

internal sealed class GitHubAdapterTestContext : IDisposable
{
  public static readonly DateTimeOffset FixedNow =
      new(2026, 8, 23, 16, 0, 0, TimeSpan.Zero);

  private readonly bool _deletePrivateKey;

  public GitHubAdapterTestContext(
      string? privateKeyPath = null,
      bool createPrivateKey = true)
  {
    Handler = new RecordingHttpMessageHandler();
    TimeProvider = new FakeTimeProvider(FixedNow);
    if (privateKeyPath is null)
    {
      privateKeyPath = Path.Combine(
          Path.GetTempPath(),
          $"pitcrew-github-app-{Guid.NewGuid():N}.pem");
      _deletePrivateKey = true;
    }
    PrivateKeyPath = privateKeyPath;

    if (createPrivateKey)
    {
      using var rsa = RSA.Create(2048);
      PrivateKeyPem = rsa.ExportRSAPrivateKeyPem();
      PublicKey = RSA.Create();
      PublicKey.ImportFromPem(rsa.ExportRSAPublicKeyPem());
      File.WriteAllText(
          PrivateKeyPath,
          PrivateKeyPem,
          new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    Options = new GitHubAppOptions
    {
      AppId = 12345,
      PrivateKeyPath = PrivateKeyPath,
      BaseAddress = new Uri("https://api.github.com/"),
      Timeout = TimeSpan.FromSeconds(30),
    };
    var options = Microsoft.Extensions.Options.Options.Create(Options);
    var factory = new RecordingHttpClientFactory(Handler);
    Signer = new GitHubAppJwtSigner(
        new GitHubPrivateKeyFileReader(options),
        options,
        TimeProvider);
    var tokenProvider = new GitHubAppTokenProvider(
        factory,
        Signer,
        options,
        TimeProvider);
    Client = new GitHubImageWorkflowClient(
        factory,
        tokenProvider,
        options,
        TimeProvider);
  }

  public RecordingHttpMessageHandler Handler { get; }

  public FakeTimeProvider TimeProvider { get; }

  public GitHubAppOptions Options { get; }

  public GitHubAppJwtSigner Signer { get; }

  public GitHubImageWorkflowClient Client { get; }

  public string PrivateKeyPath { get; }

  public string? PrivateKeyPem { get; }

  public RSA? PublicKey { get; }

  public void EnqueueToken(string token = "installation-token")
  {
    var expiresAt = FixedNow.AddMinutes(30).ToString("O");
    Handler.Enqueue(JsonResponse(
        $$"""{"token":"{{token}}","expires_at":"{{expiresAt}}"}"""));
  }

  public static HttpResponseMessage JsonResponse(
      string json,
      HttpStatusCode statusCode = HttpStatusCode.OK) =>
      new(statusCode)
      {
        Content = new StringContent(
            json,
            Encoding.UTF8,
            "application/json"),
      };

  public void Dispose()
  {
    PublicKey?.Dispose();
    Handler.Dispose();
    if (_deletePrivateKey)
    {
      try
      {
        File.Delete(PrivateKeyPath);
      }
      catch (IOException)
      {
      }
      catch (UnauthorizedAccessException)
      {
      }
    }
  }
}
