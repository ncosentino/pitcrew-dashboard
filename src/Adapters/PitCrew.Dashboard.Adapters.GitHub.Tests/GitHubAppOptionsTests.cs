using System.Collections.ObjectModel;
using System.Security.Cryptography;

using PitCrew.Dashboard.Features.Images.Abstractions;

namespace PitCrew.Dashboard.Adapters.GitHub.Tests;

public sealed class GitHubAppOptionsTests
{
  [Test]
  public async Task Disabled_Default_Options_Validate(
      CancellationToken cancellationToken)
  {
    _ = cancellationToken;
    var validator = new GitHubAppOptionsValidator();

    var errors = validator.Validate(new GitHubAppOptions()).ToArray();

    await Assert.That(errors).IsEmpty();
  }

  [Test]
  public async Task Enabled_Missing_And_Invalid_Configuration_Fails(
      CancellationToken cancellationToken)
  {
    _ = cancellationToken;
    var validator = new GitHubAppOptionsValidator();
    var options = new GitHubAppOptions
    {
      Enabled = true,
      AppId = 0,
      PrivateKeyPath = string.Empty,
      BaseAddress = new Uri("http://api.github.com/"),
      Timeout = TimeSpan.Zero,
    };

    var errors = validator.Validate(options).ToArray();
    var properties = errors
        .Select(static error => error.PropertyName)
        .ToArray();

    await Assert.That(errors).Count().IsEqualTo(4);
    await Assert.That(properties).Contains(nameof(GitHubAppOptions.AppId));
    await Assert.That(properties)
        .Contains(nameof(GitHubAppOptions.PrivateKeyPath));
    await Assert.That(properties)
        .Contains(nameof(GitHubAppOptions.BaseAddress));
    await Assert.That(properties).Contains(nameof(GitHubAppOptions.Timeout));
  }

  [Test]
  public async Task Enabled_Malformed_Private_Key_Fails_Validation(
      CancellationToken cancellationToken)
  {
    var path = Path.Combine(
        Path.GetTempPath(),
        $"malformed-options-{Guid.NewGuid():N}.pem");
    try
    {
      await File.WriteAllTextAsync(
          path,
          "not-a-private-key",
          cancellationToken);
      var validator = new GitHubAppOptionsValidator();
      var options = new GitHubAppOptions
      {
        Enabled = true,
        AppId = 12345,
        PrivateKeyPath = path,
      };

      var errors = validator.Validate(options).ToArray();

      await Assert.That(errors).Count().IsEqualTo(1);
      await Assert.That(errors[0].PropertyName)
          .IsEqualTo(nameof(GitHubAppOptions.PrivateKeyPath));
    }
    finally
    {
      File.Delete(path);
    }
  }

  [Test]
  public async Task Enabled_Public_Key_Only_Pem_Fails_Validation(
      CancellationToken cancellationToken)
  {
    var path = Path.Combine(
        Path.GetTempPath(),
        $"public-only-options-{Guid.NewGuid():N}.pem");
    try
    {
      using var rsa = RSA.Create(2048);
      await File.WriteAllTextAsync(
          path,
          rsa.ExportRSAPublicKeyPem(),
          cancellationToken);
      var validator = new GitHubAppOptionsValidator();
      var options = new GitHubAppOptions
      {
        Enabled = true,
        AppId = 12345,
        PrivateKeyPath = path,
      };

      var errors = validator.Validate(options).ToArray();

      await Assert.That(errors).Count().IsEqualTo(1);
      await Assert.That(errors[0].PropertyName)
          .IsEqualTo(nameof(GitHubAppOptions.PrivateKeyPath));
      await Assert.That(errors[0].Message).DoesNotContain(path);
    }
    finally
    {
      File.Delete(path);
    }
  }

  [Test]
  public async Task Enabled_Valid_Configuration_Validates(
      CancellationToken cancellationToken)
  {
    _ = cancellationToken;
    using var context = new GitHubAdapterTestContext();
    var validator = new GitHubAppOptionsValidator();

    var errors = validator.Validate(context.Options).ToArray();

    await Assert.That(errors).IsEmpty();
  }

  [Test]
  public async Task Disabled_Client_Returns_Not_Configured_Without_Boundary_Access(
      CancellationToken cancellationToken)
  {
    var missingPath = Path.Combine(
        Path.GetTempPath(),
        $"disabled-missing-{Guid.NewGuid():N}.pem");
    using var context = new GitHubAdapterTestContext(
        missingPath,
        createPrivateKey: false,
        enabled: false);
    var repository = new GitHubRepositoryIdentity(
        42,
        "nexus-labs",
        "pitcrew");

    var repositoryOutcome = await context.Client.LoadRepositoryAsync(
        77,
        42,
        cancellationToken);
    var workflowOutcome = await context.Client.LoadWorkflowAsync(
        77,
        repository,
        99,
        cancellationToken);
    var fileOutcome = await context.Client.LoadWorkflowFileRevisionAsync(
        77,
        repository,
        ".github/workflows/image-candidate.yml",
        "main",
        cancellationToken);
    var commitOutcome = await context.Client.ResolveCommitAsync(
        77,
        repository,
        "0123456789abcdef0123456789abcdef01234567",
        cancellationToken);
    var compareOutcome = await context.Client.VerifyCommitReachableAsync(
        77,
        repository,
        "0123456789abcdef0123456789abcdef01234567",
        "main",
        cancellationToken);
    var dispatchOutcome = await context.Client.DispatchWorkflowAsync(
        77,
        repository,
        99,
        "main",
        ReadOnlyDictionary<string, string>.Empty,
        cancellationToken);
    var runOutcome = await context.Client.LoadWorkflowRunAsync(
        77,
        repository,
        987,
        cancellationToken);
    var artifactsOutcome = await context.Client.ListWorkflowRunArtifactsAsync(
        77,
        repository,
        987,
        10,
        cancellationToken);

    var outcomes = new[]
    {
      repositoryOutcome.Kind,
      workflowOutcome.Kind,
      fileOutcome.Kind,
      commitOutcome.Kind,
      compareOutcome.Kind,
      dispatchOutcome.Kind,
      runOutcome.Kind,
      artifactsOutcome.Kind,
    };
    await Assert.That(
            outcomes.All(static outcome =>
                outcome == GitHubClientOutcomeKind.NotConfigured))
        .IsTrue()
        .Because("every disabled operation must fail before external access");
    await Assert.That(repositoryOutcome.Detail)
        .IsEqualTo("github-app-disabled");
    await Assert.That(context.Handler.Requests).IsEmpty();
    await Assert.That(File.Exists(missingPath)).IsFalse()
        .Because("the disabled client must not touch the configured key path");
  }
}
