using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Access.Abstractions;
using PitCrew.Dashboard.Features.Images.Abstractions;

namespace PitCrew.Dashboard.Adapters.Sqlite.Tests;

public sealed class SqliteImageCandidateStoreTests
{
  [Test]
  public async Task Migration_23_Applies_And_Recipe_Disable_Is_One_Way(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("migration-recipe");
    try
    {
      var context = await CreateContextAsync(
          databasePath,
          cancellationToken);
      var migrationVersion = await ExecuteScalarAsync<long>(
          context.Factory,
          "SELECT MAX(version) FROM schema_migrations;",
          cancellationToken);
      var recipe = CreateRecipe(
          context.Now,
          context.Owner.GitHubUserId);
      var created = await context.Store.CreateRecipeVersionAsync(
          recipe,
          cancellationToken);
      var exactReplay = await context.Store.CreateRecipeVersionAsync(
          recipe,
          cancellationToken);
      var conflictingIdentity = await context.Store.CreateRecipeVersionAsync(
          recipe with { WorkflowBlobSha = new string('b', 40) },
          cancellationToken);
      var disabledAt = context.Now.AddMinutes(1);
      var disabled = await context.Store.DisableRecipeVersionAsync(
          recipe.TenantId,
          recipe.RegistrationId,
          recipe.Version,
          context.Owner.GitHubUserId,
          disabledAt,
          cancellationToken);
      var disableReplay = await context.Store.DisableRecipeVersionAsync(
          recipe.TenantId,
          recipe.RegistrationId,
          recipe.Version,
          context.Owner.GitHubUserId,
          disabledAt,
          cancellationToken);
      var disableRewrite = await context.Store.DisableRecipeVersionAsync(
          recipe.TenantId,
          recipe.RegistrationId,
          recipe.Version,
          context.Owner.GitHubUserId,
          disabledAt.AddMinutes(1),
          cancellationToken);
      var stored = await context.Store.GetRecipeVersionOrNullAsync(
          recipe.TenantId,
          recipe.RegistrationId,
          recipe.Version,
          cancellationToken);

      await Assert.That(migrationVersion).IsEqualTo(23);
      await Assert.That(created)
          .IsEqualTo(ImageCandidateMutationResult.Succeeded);
      await Assert.That(exactReplay)
          .IsEqualTo(ImageCandidateMutationResult.Unchanged);
      await Assert.That(conflictingIdentity)
          .IsEqualTo(ImageCandidateMutationResult.Conflict);
      await Assert.That(disabled)
          .IsEqualTo(ImageCandidateMutationResult.Succeeded);
      await Assert.That(disableReplay)
          .IsEqualTo(ImageCandidateMutationResult.Unchanged);
      await Assert.That(disableRewrite)
          .IsEqualTo(ImageCandidateMutationResult.Conflict);
      await Assert.That(stored!.DisabledAt).IsEqualTo(disabledAt);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Recipe_And_Request_Operations_Fail_Closed_Across_Tenants(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("tenant-isolation");
    try
    {
      var context = await CreateContextAsync(
          databasePath,
          cancellationToken);
      var recipe = CreateRecipe(
          context.Now,
          context.Owner.GitHubUserId);
      await context.Store.CreateRecipeVersionAsync(recipe, cancellationToken);
      var request = CreateRequest(recipe, context.Now.AddMinutes(1));
      await context.Store.CreateBuildRequestAsync(request, cancellationToken);

      var wrongTenantRecipe = await context.Store.GetRecipeVersionOrNullAsync(
          "tenant-b",
          recipe.RegistrationId,
          recipe.Version,
          cancellationToken);
      var wrongTenantDisable = await context.Store.DisableRecipeVersionAsync(
          "tenant-b",
          recipe.RegistrationId,
          recipe.Version,
          context.Owner.GitHubUserId,
          context.Now.AddMinutes(2),
          cancellationToken);
      var wrongTenantRequest = await context.Store.GetBuildRequestOrNullAsync(
          "tenant-b",
          request.RequestId,
          cancellationToken);
      var wrongTenantTransition =
          await context.Store.ApplyBuildRequestTransitionAsync(
              "tenant-b",
              request.RequestId,
              new ImageBuildRequestTransition(
                  ImageBuildRequestStatus.Requested,
                  ImageBuildRequestStatus.Dispatching,
                  null,
                  null,
                  null,
                  null,
                  context.Now.AddMinutes(2)),
              cancellationToken);

      await Assert.That(wrongTenantRecipe).IsNull();
      await Assert.That(wrongTenantDisable)
          .IsEqualTo(ImageCandidateMutationResult.NotFound);
      await Assert.That(wrongTenantRequest).IsNull();
      await Assert.That(wrongTenantTransition)
          .IsEqualTo(ImageCandidateMutationResult.NotFound);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Request_Survives_Reopen_And_Reaches_Qualifying_Monotonically(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("request-lifecycle");
    try
    {
      var context = await CreateContextAsync(
          databasePath,
          cancellationToken);
      var recipe = CreateRecipe(
          context.Now,
          context.Owner.GitHubUserId);
      await context.Store.CreateRecipeVersionAsync(recipe, cancellationToken);
      var request = CreateRequest(recipe, context.Now.AddMinutes(1));
      var created = await context.Store.CreateBuildRequestAsync(
          request,
          cancellationToken);
      var reopenedStore = new SqliteImageCandidateStore(
          CreateFactory(databasePath));
      var reopened = await reopenedStore.GetBuildRequestOrNullAsync(
          request.TenantId,
          request.RequestId,
          cancellationToken);
      var skipped = await reopenedStore.ApplyBuildRequestTransitionAsync(
          request.TenantId,
          request.RequestId,
          new ImageBuildRequestTransition(
              ImageBuildRequestStatus.Requested,
              ImageBuildRequestStatus.Building,
              7001,
              "https://github.example/runs/7001",
              null,
              null,
              context.Now.AddMinutes(2)),
          cancellationToken);
      var dispatchingTransition = new ImageBuildRequestTransition(
          ImageBuildRequestStatus.Requested,
          ImageBuildRequestStatus.Dispatching,
          null,
          null,
          null,
          null,
          context.Now.AddMinutes(2));
      var dispatching = await reopenedStore.ApplyBuildRequestTransitionAsync(
          request.TenantId,
          request.RequestId,
          dispatchingTransition,
          cancellationToken);
      var dispatchingReplay =
          await reopenedStore.ApplyBuildRequestTransitionAsync(
              request.TenantId,
              request.RequestId,
              dispatchingTransition,
              cancellationToken);
      var building = await reopenedStore.ApplyBuildRequestTransitionAsync(
          request.TenantId,
          request.RequestId,
          new ImageBuildRequestTransition(
              ImageBuildRequestStatus.Dispatching,
              ImageBuildRequestStatus.Building,
              7001,
              "https://github.example/runs/7001",
              null,
              null,
              context.Now.AddMinutes(3)),
          cancellationToken);
      var backward = await reopenedStore.ApplyBuildRequestTransitionAsync(
          request.TenantId,
          request.RequestId,
          new ImageBuildRequestTransition(
              ImageBuildRequestStatus.Building,
              ImageBuildRequestStatus.Dispatching,
              7001,
              "https://github.example/runs/7001",
              null,
              null,
              context.Now.AddMinutes(4)),
          cancellationToken);
      var qualifying = await reopenedStore.ApplyBuildRequestTransitionAsync(
          request.TenantId,
          request.RequestId,
          new ImageBuildRequestTransition(
              ImageBuildRequestStatus.Building,
              ImageBuildRequestStatus.Qualifying,
              7001,
              "https://github.example/runs/7001",
              null,
              null,
              context.Now.AddMinutes(4)),
          cancellationToken);
      var stored = await reopenedStore.GetBuildRequestOrNullAsync(
          request.TenantId,
          request.RequestId,
          cancellationToken);

      await Assert.That(created)
          .IsEqualTo(ImageCandidateMutationResult.Succeeded);
      await Assert.That(reopened).IsEqualTo(request);
      await Assert.That(skipped)
          .IsEqualTo(ImageCandidateMutationResult.InvalidTransition);
      await Assert.That(dispatching)
          .IsEqualTo(ImageCandidateMutationResult.Succeeded);
      await Assert.That(dispatchingReplay)
          .IsEqualTo(ImageCandidateMutationResult.Unchanged);
      await Assert.That(building)
          .IsEqualTo(ImageCandidateMutationResult.Succeeded);
      await Assert.That(backward)
          .IsEqualTo(ImageCandidateMutationResult.InvalidTransition);
      await Assert.That(qualifying)
          .IsEqualTo(ImageCandidateMutationResult.Succeeded);
      await Assert.That(stored!.Status)
          .IsEqualTo(ImageBuildRequestStatus.Qualifying);
      await Assert.That(stored.GitHubRunId).IsEqualTo(7001);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Ready_Candidate_And_Qualifications_Commit_Atomically(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("ready-candidate");
    try
    {
      var context = await CreateQualifyingContextAsync(
          databasePath,
          cancellationToken);
      var requestAuthority = context.Request!;
      var candidate = CreateReadyCandidate(
          requestAuthority,
          context.Now.AddMinutes(5));
      var qualifications = CreatePassedQualifications(candidate.CandidateId);
      var stored = await context.Store.StoreCandidateAsync(
          requestAuthority.TenantId,
          candidate,
          qualifications,
          cancellationToken);
      var replay = await context.Store.StoreCandidateAsync(
          requestAuthority.TenantId,
          candidate,
          qualifications,
          cancellationToken);
      var conflict = await context.Store.StoreCandidateAsync(
          requestAuthority.TenantId,
          candidate with { ArtifactId = candidate.ArtifactId + 1 },
          qualifications,
          cancellationToken);
      var request = await context.Store.GetBuildRequestOrNullAsync(
          requestAuthority.TenantId,
          requestAuthority.RequestId,
          cancellationToken);
      var candidateCount = await ExecuteScalarAsync<long>(
          context.Factory,
          "SELECT COUNT(*) FROM image_candidates;",
          cancellationToken);
      var qualificationCount = await ExecuteScalarAsync<long>(
          context.Factory,
          "SELECT COUNT(*) FROM image_candidate_qualifications;",
          cancellationToken);
      var terminalRewrite =
          await context.Store.ApplyBuildRequestTransitionAsync(
              requestAuthority.TenantId,
              requestAuthority.RequestId,
              new ImageBuildRequestTransition(
                  ImageBuildRequestStatus.Ready,
                  ImageBuildRequestStatus.Blocked,
                  candidate.GitHubRunId,
                  "https://github.example/runs/7001",
                  "rewrite",
                  "Terminal requests are immutable.",
                  context.Now.AddMinutes(6)),
              cancellationToken);

      await Assert.That(stored)
          .IsEqualTo(ImageCandidateMutationResult.Succeeded);
      await Assert.That(replay)
          .IsEqualTo(ImageCandidateMutationResult.Unchanged);
      await Assert.That(conflict)
          .IsEqualTo(ImageCandidateMutationResult.Conflict);
      await Assert.That(request!.Status)
          .IsEqualTo(ImageBuildRequestStatus.Ready);
      await Assert.That(candidateCount).IsEqualTo(1);
      await Assert.That(qualificationCount).IsEqualTo(4);
      await Assert.That(terminalRewrite)
          .IsEqualTo(ImageCandidateMutationResult.InvalidTransition);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Invalid_Ready_Candidate_Does_Not_Partially_Commit(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("ready-validation");
    try
    {
      var context = await CreateQualifyingContextAsync(
          databasePath,
          cancellationToken);
      var requestAuthority = context.Request!;
      var candidate = CreateReadyCandidate(
          requestAuthority,
          context.Now.AddMinutes(5));
      var missingDigest = candidate with { Digest = string.Empty };
      var badQualifications = CreatePassedQualifications(candidate.CandidateId)
          .Select((qualification, index) =>
              index == 0
                  ? qualification with
                  {
                    Status = ImageCandidateQualificationStatus.Failed,
                  }
                  : qualification)
          .ToArray();
      var missingDigestResult = await context.Store.StoreCandidateAsync(
          requestAuthority.TenantId,
          missingDigest,
          CreatePassedQualifications(candidate.CandidateId),
          cancellationToken);
      var badQualificationResult = await context.Store.StoreCandidateAsync(
          requestAuthority.TenantId,
          candidate,
          badQualifications,
          cancellationToken);
      var candidateCount = await ExecuteScalarAsync<long>(
          context.Factory,
          "SELECT COUNT(*) FROM image_candidates;",
          cancellationToken);
      var request = await context.Store.GetBuildRequestOrNullAsync(
          requestAuthority.TenantId,
          requestAuthority.RequestId,
          cancellationToken);

      await Assert.That(missingDigestResult)
          .IsEqualTo(ImageCandidateMutationResult.Conflict);
      await Assert.That(badQualificationResult)
          .IsEqualTo(ImageCandidateMutationResult.Conflict);
      await Assert.That(candidateCount).IsEqualTo(0);
      await Assert.That(request!.Status)
          .IsEqualTo(ImageBuildRequestStatus.Qualifying);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Failed_Candidate_Allows_Null_Digest_With_Closed_Evidence(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("failed-candidate");
    try
    {
      var context = await CreateQualifyingContextAsync(
          databasePath,
          cancellationToken);
      var requestAuthority = context.Request!;
      var reportJson = "{\"schemaVersion\":1,\"status\":\"failed\"}";
      var candidate = new FailedImageCandidate(
          Guid.NewGuid(),
          requestAuthority.TenantId,
          requestAuthority.RequestId,
          requestAuthority.RecipeId,
          requestAuthority.SourceRepository,
          requestAuthority.SourceCommit,
          7001,
          8001,
          "pitcrew-image-candidate",
          $"sha256:{new string('c', 64)}",
          Sha256(reportJson),
          reportJson,
          "ghcr.io/example/pitcrew:test",
          ImageCandidatePlatform.LinuxAmd64,
          ImageCandidateOutputMode.Registry,
          context.Now.AddMinutes(4),
          context.Now.AddMinutes(5),
          null,
          null,
          "build-failed",
          "Image build did not complete.");
      var qualifications = CreatePassedQualifications(candidate.CandidateId)
          .Select((qualification, index) =>
              index == 0
                  ? qualification with
                  {
                    Status = ImageCandidateQualificationStatus.Failed,
                  }
                  : qualification)
          .ToArray();
      var result = await context.Store.StoreCandidateAsync(
          requestAuthority.TenantId,
          candidate,
          qualifications,
          cancellationToken);
      var request = await context.Store.GetBuildRequestOrNullAsync(
          requestAuthority.TenantId,
          requestAuthority.RequestId,
          cancellationToken);

      await Assert.That(result)
          .IsEqualTo(ImageCandidateMutationResult.Succeeded);
      await Assert.That(request!.Status)
          .IsEqualTo(ImageBuildRequestStatus.Failed);
      await Assert.That(request.TerminalCategory)
          .IsEqualTo(candidate.FailureCategory);
      await Assert.That(request.TerminalDetail)
          .IsEqualTo(candidate.FailureDetail);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Storage_Constraints_Reject_Malformed_And_Unbounded_Values(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("constraints");
    try
    {
      var context = await CreateContextAsync(
          databasePath,
          cancellationToken);
      var recipe = CreateRecipe(
          context.Now,
          context.Owner.GitHubUserId);
      var malformedRecipe = await context.Store.CreateRecipeVersionAsync(
          recipe with { RepositoryOwner = new string('x', 101) },
          cancellationToken);
      await context.Store.CreateRecipeVersionAsync(recipe, cancellationToken);
      var request = CreateRequest(recipe, context.Now.AddMinutes(1));
      var malformedRequest = await context.Store.CreateBuildRequestAsync(
          request with { SourceCommit = new string('A', 40) },
          cancellationToken);
      await context.Store.CreateBuildRequestAsync(request, cancellationToken);
      await AdvanceToQualifyingAsync(
          context.Store,
          request,
          context.Now,
          cancellationToken);
      var reportJson = $"{{\"detail\":\"{new string('x', 33000)}\"}}";
      var candidate = CreateReadyCandidate(
          request,
          context.Now.AddMinutes(5)) with
      {
        ReportJson = reportJson,
        ReportHash = Sha256(reportJson),
      };
      var unboundedCandidate = await context.Store.StoreCandidateAsync(
          request.TenantId,
          candidate,
          CreatePassedQualifications(candidate.CandidateId),
          cancellationToken);
      var candidateCount = await ExecuteScalarAsync<long>(
          context.Factory,
          "SELECT COUNT(*) FROM image_candidates;",
          cancellationToken);

      await Assert.That(malformedRecipe)
          .IsEqualTo(ImageCandidateMutationResult.Conflict);
      await Assert.That(malformedRequest)
          .IsEqualTo(ImageCandidateMutationResult.Conflict);
      await Assert.That(unboundedCandidate)
          .IsEqualTo(ImageCandidateMutationResult.Conflict);
      await Assert.That(candidateCount).IsEqualTo(0);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Workflow_Failure_Terminalizes_Without_Candidate_Evidence(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("workflow-failure");
    try
    {
      var context = await CreateQualifyingContextAsync(
          databasePath,
          cancellationToken);
      var request = context.Request!;
      var transition = new ImageBuildRequestTransition(
          ImageBuildRequestStatus.Qualifying,
          ImageBuildRequestStatus.Failed,
          7001,
          "https://github.example/runs/7001",
          "artifact-missing",
          "The required candidate artifact was not published.",
          context.Now.AddMinutes(5));
      var failed = await context.Store.ApplyBuildRequestTransitionAsync(
          request.TenantId,
          request.RequestId,
          transition,
          cancellationToken);
      var replay = await context.Store.ApplyBuildRequestTransitionAsync(
          request.TenantId,
          request.RequestId,
          transition,
          cancellationToken);
      var conflictingEvidence =
          await context.Store.ApplyBuildRequestTransitionAsync(
              request.TenantId,
              request.RequestId,
              transition with
              {
                TerminalCategory = "workflow-failed",
                TerminalDetail = "The workflow failed before artifact publication.",
              },
              cancellationToken);
      var stored = await context.Store.GetBuildRequestOrNullAsync(
          request.TenantId,
          request.RequestId,
          cancellationToken);
      var candidateCount = await ExecuteScalarAsync<long>(
          context.Factory,
          "SELECT COUNT(*) FROM image_candidates;",
          cancellationToken);

      await Assert.That(failed)
          .IsEqualTo(ImageCandidateMutationResult.Succeeded);
      await Assert.That(replay)
          .IsEqualTo(ImageCandidateMutationResult.Unchanged);
      await Assert.That(conflictingEvidence)
          .IsEqualTo(ImageCandidateMutationResult.Conflict);
      await Assert.That(stored!.Status)
          .IsEqualTo(ImageBuildRequestStatus.Failed);
      await Assert.That(stored.TerminalCategory)
          .IsEqualTo(transition.TerminalCategory);
      await Assert.That(stored.TerminalDetail)
          .IsEqualTo(transition.TerminalDetail);
      await Assert.That(candidateCount).IsEqualTo(0);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Purge_Is_Age_And_Count_Bounded_And_Preserves_Active_Requests(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("retention");
    try
    {
      var context = await CreateContextAsync(
          databasePath,
          cancellationToken);
      var recipe = CreateRecipe(
          context.Now,
          context.Owner.GitHubUserId);
      await context.Store.CreateRecipeVersionAsync(recipe, cancellationToken);

      var readyRequest = CreateRequest(recipe, context.Now.AddMinutes(1));
      await context.Store.CreateBuildRequestAsync(
          readyRequest,
          cancellationToken);
      await AdvanceToQualifyingAsync(
          context.Store,
          readyRequest,
          context.Now,
          cancellationToken);
      var readyCandidate = CreateReadyCandidate(
          readyRequest,
          context.Now.AddMinutes(5));
      await context.Store.StoreCandidateAsync(
          readyRequest.TenantId,
          readyCandidate,
          CreatePassedQualifications(readyCandidate.CandidateId),
          cancellationToken);

      var oldFailedRequest = CreateRequest(
          recipe,
          context.Now.AddMinutes(2));
      await context.Store.CreateBuildRequestAsync(
          oldFailedRequest,
          cancellationToken);
      await AdvanceToFailedAsync(
          context.Store,
          oldFailedRequest,
          context.Now.AddMinutes(1),
          cancellationToken);

      var recentFailedRequest = CreateRequest(
          recipe,
          context.Now.AddMinutes(20));
      await context.Store.CreateBuildRequestAsync(
          recentFailedRequest,
          cancellationToken);
      await AdvanceToFailedAsync(
          context.Store,
          recentFailedRequest,
          context.Now.AddMinutes(20),
          cancellationToken);

      var activeRequest = CreateRequest(
          recipe,
          context.Now.AddMinutes(3));
      await context.Store.CreateBuildRequestAsync(
          activeRequest,
          cancellationToken);

      var firstDeleted = await context.Store.PurgeTerminalBuildRequestsAsync(
          recipe.TenantId,
          context.Now.AddMinutes(10),
          1,
          cancellationToken);
      var candidateCountAfterFirst = await ExecuteScalarAsync<long>(
          context.Factory,
          "SELECT COUNT(*) FROM image_candidates;",
          cancellationToken);
      var qualificationCountAfterFirst = await ExecuteScalarAsync<long>(
          context.Factory,
          "SELECT COUNT(*) FROM image_candidate_qualifications;",
          cancellationToken);
      var secondDeleted = await context.Store.PurgeTerminalBuildRequestsAsync(
          recipe.TenantId,
          context.Now.AddMinutes(10),
          500,
          cancellationToken);
      var remaining = await context.Store.ListBuildRequestsAsync(
          recipe.TenantId,
          null,
          200,
          cancellationToken);

      await Assert.That(firstDeleted).IsEqualTo(1);
      await Assert.That(candidateCountAfterFirst).IsEqualTo(0);
      await Assert.That(qualificationCountAfterFirst).IsEqualTo(0);
      await Assert.That(secondDeleted).IsEqualTo(1);
      await Assert.That(remaining).Count().IsEqualTo(2);
      await Assert.That(remaining.Select(item => item.RequestId))
          .Contains(recentFailedRequest.RequestId);
      await Assert.That(remaining.Select(item => item.RequestId))
          .Contains(activeRequest.RequestId);
      await Assert.That(remaining.Single(
              item => item.RequestId == activeRequest.RequestId).Status)
          .IsEqualTo(ImageBuildRequestStatus.Requested);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Tenant_Delete_Cascades_Candidate_Domain_History(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("tenant-cascade");
    try
    {
      var context = await CreateQualifyingContextAsync(
          databasePath,
          cancellationToken);
      var request = context.Request!;
      var candidate = CreateReadyCandidate(
          request,
          context.Now.AddMinutes(5));
      await context.Store.StoreCandidateAsync(
          request.TenantId,
          candidate,
          CreatePassedQualifications(candidate.CandidateId),
          cancellationToken);

      var deleted = await ExecuteNonQueryAsync(
          context.Factory,
          "DELETE FROM tenants WHERE tenant_id = 'tenant-a';",
          cancellationToken);
      var recipeCount = await ExecuteScalarAsync<long>(
          context.Factory,
          "SELECT COUNT(*) FROM image_recipe_versions;",
          cancellationToken);
      var requestCount = await ExecuteScalarAsync<long>(
          context.Factory,
          "SELECT COUNT(*) FROM image_build_requests;",
          cancellationToken);
      var candidateCount = await ExecuteScalarAsync<long>(
          context.Factory,
          "SELECT COUNT(*) FROM image_candidates;",
          cancellationToken);
      var qualificationCount = await ExecuteScalarAsync<long>(
          context.Factory,
          "SELECT COUNT(*) FROM image_candidate_qualifications;",
          cancellationToken);

      await Assert.That(deleted).IsEqualTo(1);
      await Assert.That(recipeCount).IsEqualTo(0);
      await Assert.That(requestCount).IsEqualTo(0);
      await Assert.That(candidateCount).IsEqualTo(0);
      await Assert.That(qualificationCount).IsEqualTo(0);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Request_Must_Match_Frozen_Recipe_And_Repository_Authority(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("recipe-authority");
    try
    {
      var context = await CreateContextAsync(
          databasePath,
          cancellationToken);
      var recipe = CreateRecipe(
          context.Now,
          context.Owner.GitHubUserId);
      await context.Store.CreateRecipeVersionAsync(recipe, cancellationToken);
      var request = CreateRequest(recipe, context.Now.AddMinutes(1));
      var wrongRecipe = await context.Store.CreateBuildRequestAsync(
          request with
          {
            RequestId = Guid.NewGuid(),
            RecipeId = "other-valid-recipe",
          },
          cancellationToken);
      var wrongRepository = await context.Store.CreateBuildRequestAsync(
          request with
          {
            RequestId = Guid.NewGuid(),
            SourceRepository = "ncosentino/other-valid-repository",
          },
          cancellationToken);
      var requestCount = await ExecuteScalarAsync<long>(
          context.Factory,
          "SELECT COUNT(*) FROM image_build_requests;",
          cancellationToken);

      await Assert.That(wrongRecipe)
          .IsEqualTo(ImageCandidateMutationResult.Conflict);
      await Assert.That(wrongRepository)
          .IsEqualTo(ImageCandidateMutationResult.Conflict);
      await Assert.That(requestCount).IsEqualTo(0);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Concurrent_Exact_Creates_And_Disables_Converge(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("concurrent-replay");
    try
    {
      var context = await CreateContextAsync(
          databasePath,
          cancellationToken);
      var recipe = CreateRecipe(
          context.Now,
          context.Owner.GitHubUserId);
      var recipeResults = await Task.WhenAll(
          context.Store.CreateRecipeVersionAsync(recipe, cancellationToken),
          context.Store.CreateRecipeVersionAsync(recipe, cancellationToken));

      var request = CreateRequest(recipe, context.Now.AddMinutes(1));
      var requestResults = await Task.WhenAll(
          context.Store.CreateBuildRequestAsync(request, cancellationToken),
          context.Store.CreateBuildRequestAsync(request, cancellationToken));

      var disabledAt = context.Now.AddMinutes(2);
      var disableResults = await Task.WhenAll(
          context.Store.DisableRecipeVersionAsync(
              recipe.TenantId,
              recipe.RegistrationId,
              recipe.Version,
              context.Owner.GitHubUserId,
              disabledAt,
              cancellationToken),
          context.Store.DisableRecipeVersionAsync(
              recipe.TenantId,
              recipe.RegistrationId,
              recipe.Version,
              context.Owner.GitHubUserId,
              disabledAt,
              cancellationToken));

      await Assert.That(recipeResults.Count(
              result => result == ImageCandidateMutationResult.Succeeded))
          .IsEqualTo(1);
      await Assert.That(recipeResults.Count(
              result => result == ImageCandidateMutationResult.Unchanged))
          .IsEqualTo(1);
      await Assert.That(requestResults.Count(
              result => result == ImageCandidateMutationResult.Succeeded))
          .IsEqualTo(1);
      await Assert.That(requestResults.Count(
              result => result == ImageCandidateMutationResult.Unchanged))
          .IsEqualTo(1);
      await Assert.That(disableResults.Count(
              result => result == ImageCandidateMutationResult.Succeeded))
          .IsEqualTo(1);
      await Assert.That(disableResults.Count(
              result => result == ImageCandidateMutationResult.Unchanged))
          .IsEqualTo(1);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Retention_Orders_Different_Offsets_By_Instant(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("retention-offsets");
    try
    {
      var context = await CreateContextAsync(
          databasePath,
          cancellationToken);
      var recipe = CreateRecipe(
          context.Now,
          context.Owner.GitHubUserId);
      await context.Store.CreateRecipeVersionAsync(recipe, cancellationToken);

      var olderBase = new DateTimeOffset(
          2026,
          8,
          23,
          12,
          55,
          0,
          TimeSpan.FromHours(14));
      var olderRequest = CreateRequest(recipe, olderBase);
      await context.Store.CreateBuildRequestAsync(
          olderRequest,
          cancellationToken);
      await AdvanceToFailedAsync(
          context.Store,
          olderRequest,
          olderBase,
          cancellationToken);

      var newerBase = new DateTimeOffset(
          2026,
          8,
          22,
          14,
          55,
          0,
          TimeSpan.FromHours(-10));
      var newerRequest = CreateRequest(recipe, newerBase);
      await context.Store.CreateBuildRequestAsync(
          newerRequest,
          cancellationToken);
      await AdvanceToFailedAsync(
          context.Store,
          newerRequest,
          newerBase,
          cancellationToken);

      var storedOlder = await context.Store.GetBuildRequestOrNullAsync(
          recipe.TenantId,
          olderRequest.RequestId,
          cancellationToken);
      var storedNewer = await context.Store.GetBuildRequestOrNullAsync(
          recipe.TenantId,
          newerRequest.RequestId,
          cancellationToken);
      var cutoff = new DateTimeOffset(
          2026,
          8,
          23,
          0,
          0,
          0,
          TimeSpan.Zero);
      var deleted = await context.Store.PurgeTerminalBuildRequestsAsync(
          recipe.TenantId,
          cutoff,
          200,
          cancellationToken);
      var purgedOlder = await context.Store.GetBuildRequestOrNullAsync(
          recipe.TenantId,
          olderRequest.RequestId,
          cancellationToken);
      var preservedNewer = await context.Store.GetBuildRequestOrNullAsync(
          recipe.TenantId,
          newerRequest.RequestId,
          cancellationToken);

      await Assert.That(storedOlder!.UpdatedAt)
          .IsEqualTo(new DateTimeOffset(
              2026,
              8,
              22,
              23,
              0,
              0,
              TimeSpan.Zero));
      await Assert.That(storedOlder.UpdatedAt.Offset)
          .IsEqualTo(TimeSpan.Zero);
      await Assert.That(storedNewer!.UpdatedAt)
          .IsEqualTo(new DateTimeOffset(
              2026,
              8,
              23,
              1,
              0,
              0,
              TimeSpan.Zero));
      await Assert.That(storedNewer.UpdatedAt.Offset)
          .IsEqualTo(TimeSpan.Zero);
      await Assert.That(deleted).IsEqualTo(1);
      await Assert.That(purgedOlder).IsNull();
      await Assert.That(preservedNewer).IsNotNull();
      await Assert.That(preservedNewer!.UpdatedAt)
          .IsEqualTo(storedNewer.UpdatedAt);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  private static async Task<ImageCandidateTestContext> CreateContextAsync(
      string databasePath,
      CancellationToken cancellationToken)
  {
    var factory = CreateFactory(databasePath);
    await new SqliteMigrationRunner(factory).ApplyAsync(cancellationToken);
    var accessStore = new SqliteAccessStore(factory);
    var now = DateTimeOffset.Parse(
        "2026-08-23T12:00:00+00:00",
        CultureInfo.InvariantCulture);
    var owner = new DashboardUser(
        $"user-{Guid.NewGuid():N}",
        $"owner-{Guid.NewGuid():N}",
        "Owner",
        null);
    await accessStore.EnsureTenantOwnerAsync(
        "tenant-a",
        "Tenant A",
        owner,
        now,
        cancellationToken);
    await accessStore.EnsureTenantOwnerAsync(
        "tenant-b",
        "Tenant B",
        owner,
        now,
        cancellationToken);
    return new ImageCandidateTestContext(
        factory,
        new SqliteImageCandidateStore(factory),
        owner,
        now,
        null);
  }

  private static async Task<ImageCandidateTestContext> CreateQualifyingContextAsync(
      string databasePath,
      CancellationToken cancellationToken)
  {
    var context = await CreateContextAsync(databasePath, cancellationToken);
    var recipe = CreateRecipe(
        context.Now,
        context.Owner.GitHubUserId);
    await context.Store.CreateRecipeVersionAsync(recipe, cancellationToken);
    var request = CreateRequest(recipe, context.Now.AddMinutes(1));
    await context.Store.CreateBuildRequestAsync(request, cancellationToken);
    await AdvanceToQualifyingAsync(
        context.Store,
        request,
        context.Now,
        cancellationToken);
    return context with { Request = request };
  }

  private static async Task AdvanceToQualifyingAsync(
      SqliteImageCandidateStore store,
      ImageBuildRequest request,
      DateTimeOffset now,
      CancellationToken cancellationToken)
  {
    await store.ApplyBuildRequestTransitionAsync(
        request.TenantId,
        request.RequestId,
        new ImageBuildRequestTransition(
            ImageBuildRequestStatus.Requested,
            ImageBuildRequestStatus.Dispatching,
            null,
            null,
            null,
            null,
            now.AddMinutes(2)),
        cancellationToken);
    await store.ApplyBuildRequestTransitionAsync(
        request.TenantId,
        request.RequestId,
        new ImageBuildRequestTransition(
            ImageBuildRequestStatus.Dispatching,
            ImageBuildRequestStatus.Building,
            7001,
            "https://github.example/runs/7001",
            null,
            null,
            now.AddMinutes(3)),
        cancellationToken);
    await store.ApplyBuildRequestTransitionAsync(
        request.TenantId,
        request.RequestId,
        new ImageBuildRequestTransition(
            ImageBuildRequestStatus.Building,
            ImageBuildRequestStatus.Qualifying,
            7001,
            "https://github.example/runs/7001",
            null,
            null,
            now.AddMinutes(4)),
        cancellationToken);
  }

  private static async Task AdvanceToFailedAsync(
      SqliteImageCandidateStore store,
      ImageBuildRequest request,
      DateTimeOffset transitionBase,
      CancellationToken cancellationToken)
  {
    await AdvanceToQualifyingAsync(
        store,
        request,
        transitionBase,
        cancellationToken);
    await store.ApplyBuildRequestTransitionAsync(
        request.TenantId,
        request.RequestId,
        new ImageBuildRequestTransition(
            ImageBuildRequestStatus.Qualifying,
            ImageBuildRequestStatus.Failed,
            7001,
            "https://github.example/runs/7001",
            "artifact-missing",
            "The required candidate artifact was not published.",
            transitionBase.AddMinutes(5)),
        cancellationToken);
  }

  private static ImageRecipeRegistration CreateRecipe(
      DateTimeOffset now,
      string createdByGitHubUserId) =>
      new(
          "tenant-a",
          Guid.NewGuid(),
          1,
          1001,
          2001,
          3001,
          "ncosentino",
          "pitcrew",
          ".github/workflows/image-candidate.yml",
          new string('a', 40),
          "main",
          "pitcrew-default",
          1,
          "{\"allowedRefs\":[\"refs/heads/main\"]}",
          "{\"type\":\"object\",\"additionalProperties\":false}",
          createdByGitHubUserId,
          now,
          null,
          null);

  private static ImageBuildRequest CreateRequest(
      ImageRecipeRegistration recipe,
      DateTimeOffset requestedAt)
  {
    var inputJson = "{\"tag\":\"test\"}";
    return new ImageBuildRequest(
        recipe.TenantId,
        Guid.NewGuid(),
        recipe.RegistrationId,
        recipe.Version,
        recipe.RecipeId,
        $"{recipe.RepositoryOwner}/{recipe.RepositoryName}",
        new string('b', 40),
        inputJson,
        Sha256(inputJson),
        recipe.CreatedByGitHubUserId,
        requestedAt,
        ImageBuildRequestStatus.Requested,
        null,
        null,
        null,
        null,
        requestedAt);
  }

  private static ReadyImageCandidate CreateReadyCandidate(
      ImageBuildRequest request,
      DateTimeOffset storedAt)
  {
    var reportJson = "{\"schemaVersion\":1,\"status\":\"ready\"}";
    var digest = $"sha256:{new string('d', 64)}";
    return new ReadyImageCandidate(
        Guid.NewGuid(),
        request.TenantId,
        request.RequestId,
        request.RecipeId,
        request.SourceRepository,
        request.SourceCommit,
        7001,
        8001,
        "pitcrew-image-candidate",
        $"sha256:{new string('c', 64)}",
        Sha256(reportJson),
        reportJson,
        "ghcr.io/example/pitcrew:test",
        ImageCandidatePlatform.LinuxAmd64,
        ImageCandidateOutputMode.Registry,
        storedAt.AddMinutes(-1),
        storedAt,
        digest,
        $"ghcr.io/example/pitcrew@{digest}");
  }

  private static IReadOnlyList<ImageCandidateQualification> CreatePassedQualifications(
      Guid candidateId) =>
      [
        new(
            candidateId,
            ImageCandidateQualificationName.ImageBuild,
            ImageCandidateQualificationStatus.Passed),
        new(
            candidateId,
            ImageCandidateQualificationName.BuildKitDigest,
            ImageCandidateQualificationStatus.Passed),
        new(
            candidateId,
            ImageCandidateQualificationName.RegistryDigest,
            ImageCandidateQualificationStatus.Passed),
        new(
            candidateId,
            ImageCandidateQualificationName.BuilderCleanup,
            ImageCandidateQualificationStatus.Passed),
      ];

  private static SqliteConnectionFactory CreateFactory(string databasePath) =>
      new(Options.Create(new SqliteFleetStoreOptions
      {
        DatabasePath = databasePath,
      }));

  private static string CreateDatabasePath(string scope) =>
      Path.Combine(
          Path.GetTempPath(),
          $"pitcrew-image-{scope}-{Guid.NewGuid():N}.db");

  private static string Sha256(string value) =>
      Convert.ToHexString(
          SHA256.HashData(Encoding.UTF8.GetBytes(value)))
      .ToLowerInvariant();

  private static async Task<T> ExecuteScalarAsync<T>(
      SqliteConnectionFactory factory,
      string commandText,
      CancellationToken cancellationToken)
  {
    await using var connection = await factory.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = commandText;
    return (T)Convert.ChangeType(
        await command.ExecuteScalarAsync(cancellationToken),
        typeof(T),
        CultureInfo.InvariantCulture)!;
  }

  private static async Task<int> ExecuteNonQueryAsync(
      SqliteConnectionFactory factory,
      string commandText,
      CancellationToken cancellationToken)
  {
    await using var connection = await factory.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = commandText;
    return await command.ExecuteNonQueryAsync(cancellationToken);
  }

  private sealed record ImageCandidateTestContext(
      SqliteConnectionFactory Factory,
      SqliteImageCandidateStore Store,
      DashboardUser Owner,
      DateTimeOffset Now,
      ImageBuildRequest? Request);
}
