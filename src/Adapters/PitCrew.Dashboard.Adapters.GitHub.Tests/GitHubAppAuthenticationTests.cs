using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using PitCrew.Dashboard.Features.Images.Abstractions;

namespace PitCrew.Dashboard.Adapters.GitHub.Tests;

public sealed class GitHubAppAuthenticationTests
{
  [Test]
  public async Task Jwt_Uses_Fixed_Issuer_Window_And_RS256(
      CancellationToken cancellationToken)
  {
    using var context = new GitHubAdapterTestContext();

    var outcome = await context.Signer.CreateAsync(cancellationToken);

    await Assert.That(outcome.Kind)
        .IsEqualTo(GitHubClientOutcomeKind.Success);
    var jwt = outcome.Value!;
    var parts = jwt.Split('.');
    await Assert.That(parts).Count().IsEqualTo(3);
    using var header = JsonDocument.Parse(JwtTestDecoder.Decode(parts[0]));
    using var payload = JsonDocument.Parse(JwtTestDecoder.Decode(parts[1]));
    await Assert.That(header.RootElement.GetProperty("alg").GetString())
        .IsEqualTo("RS256");
    await Assert.That(payload.RootElement.GetProperty("iss").GetInt64())
        .IsEqualTo(context.Options.AppId);
    await Assert.That(payload.RootElement.GetProperty("iat").GetInt64())
        .IsEqualTo(
            GitHubAdapterTestContext.FixedNow.AddSeconds(-30).ToUnixTimeSeconds());
    await Assert.That(payload.RootElement.GetProperty("exp").GetInt64())
        .IsEqualTo(
            GitHubAdapterTestContext.FixedNow.AddMinutes(9).ToUnixTimeSeconds());

    var signingInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
    var valid = context.PublicKey!.VerifyData(
        signingInput,
        JwtTestDecoder.Decode(parts[2]),
        HashAlgorithmName.SHA256,
        RSASignaturePadding.Pkcs1);
    await Assert.That(valid).IsTrue()
        .Because("the generated JWT must carry an RS256 signature");
    await Assert.That(jwt.Contains(context.PrivateKeyPath, StringComparison.Ordinal))
        .IsFalse()
        .Because("the JWT must not expose the configured key path");
    await Assert.That(
            jwt.Contains(context.PrivateKeyPem!, StringComparison.Ordinal))
        .IsFalse()
        .Because("the JWT must not expose private-key material");
  }

  [Test]
  public async Task Installation_Token_Is_Repository_And_Permission_Restricted(
      CancellationToken cancellationToken)
  {
    using var context = new GitHubAdapterTestContext();
    context.EnqueueToken("secret-installation-token");
    context.Handler.Enqueue(
        GitHubAdapterTestContext.JsonResponse(
            """{"id":42,"name":"pitcrew","owner":{"login":"nexus-labs"}}"""));

    var outcome = await context.Client.LoadRepositoryAsync(
        77,
        42,
        cancellationToken);

    await Assert.That(outcome.Kind)
        .IsEqualTo(GitHubClientOutcomeKind.Success);
    await Assert.That(context.Handler.Requests).Count().IsEqualTo(2);
    var tokenRequest = context.Handler.Requests[0];
    await Assert.That(tokenRequest.Method).IsEqualTo(HttpMethod.Post);
    await Assert.That(tokenRequest.Uri.AbsolutePath)
        .IsEqualTo("/app/installations/77/access_tokens");
    await Assert.That(tokenRequest.Headers["Accept"])
        .Contains("application/vnd.github+json");
    await Assert.That(tokenRequest.Headers["X-GitHub-Api-Version"])
        .IsEqualTo(GitHubImageWorkflowClient.ApiVersion);
    await Assert.That(tokenRequest.Headers["User-Agent"])
        .Contains("PitCrew-Dashboard-GitHubApp/1");
    await Assert.That(tokenRequest.Headers["Authorization"])
        .StartsWith("Bearer ");

    using var body = JsonDocument.Parse(tokenRequest.Body!);
    var repositoryIds = body.RootElement.GetProperty("repository_ids");
    await Assert.That(repositoryIds.GetArrayLength()).IsEqualTo(1);
    await Assert.That(repositoryIds[0].GetInt64()).IsEqualTo(42);
    var permissions = body.RootElement.GetProperty("permissions");
    await Assert.That(permissions.EnumerateObject().Count()).IsEqualTo(2);
    await Assert.That(permissions.GetProperty("actions").GetString())
        .IsEqualTo("write");
    await Assert.That(permissions.GetProperty("contents").GetString())
        .IsEqualTo("read");

    var operationRequest = context.Handler.Requests[1];
    await Assert.That(operationRequest.Uri.AbsolutePath)
        .IsEqualTo("/repositories/42");
    await Assert.That(operationRequest.Headers["Authorization"])
        .IsEqualTo("Bearer secret-installation-token");
    await Assert.That(operationRequest.Headers["X-GitHub-Api-Version"])
        .IsEqualTo(GitHubImageWorkflowClient.ApiVersion);
    await Assert.That(outcome.Value!.Id).IsEqualTo(42);
    await Assert.That(outcome.Value.Owner).IsEqualTo("nexus-labs");
    await Assert.That(outcome.Detail).IsNull();
    await Assert.That(outcome.ToString())
        .DoesNotContain("secret-installation-token");
  }

  [Test]
  public async Task Private_Key_Failures_Are_Explicit_And_Redacted(
      CancellationToken cancellationToken)
  {
    var missingPath = Path.Combine(
        Path.GetTempPath(),
        $"missing-{Guid.NewGuid():N}.pem");
    using var missing = new GitHubAdapterTestContext(
        missingPath,
        createPrivateKey: false);
    var missingOutcome = await missing.Client.LoadRepositoryAsync(
        77,
        42,
        cancellationToken);

    var oversizedPath = Path.Combine(
        Path.GetTempPath(),
        $"oversized-{Guid.NewGuid():N}.pem");
    var malformedPath = Path.Combine(
        Path.GetTempPath(),
        $"malformed-{Guid.NewGuid():N}.pem");
    try
    {
      await File.WriteAllBytesAsync(
          oversizedPath,
          new byte[GitHubAppOptionsValidator.MaximumPrivateKeyBytes + 1],
          cancellationToken);
      await File.WriteAllTextAsync(
          malformedPath,
          "not-a-private-key",
          cancellationToken);
      using var oversized = new GitHubAdapterTestContext(
          oversizedPath,
          createPrivateKey: false);
      using var malformed = new GitHubAdapterTestContext(
          malformedPath,
          createPrivateKey: false);

      var oversizedOutcome = await oversized.Client.LoadRepositoryAsync(
          77,
          42,
          cancellationToken);
      var malformedOutcome = await malformed.Client.LoadRepositoryAsync(
          77,
          42,
          cancellationToken);

      await Assert.That(missingOutcome.Kind)
          .IsEqualTo(GitHubClientOutcomeKind.InvalidRequest);
      await Assert.That(oversizedOutcome.Kind)
          .IsEqualTo(GitHubClientOutcomeKind.InvalidRequest);
      await Assert.That(malformedOutcome.Kind)
          .IsEqualTo(GitHubClientOutcomeKind.InvalidRequest);
      await Assert.That(missingOutcome.Detail).DoesNotContain(missingPath);
      await Assert.That(oversizedOutcome.Detail).DoesNotContain(oversizedPath);
      await Assert.That(malformedOutcome.Detail).DoesNotContain(malformedPath);
      await Assert.That(malformedOutcome.Detail)
          .DoesNotContain("not-a-private-key");
    }
    finally
    {
      File.Delete(oversizedPath);
      File.Delete(malformedPath);
    }
  }
}
