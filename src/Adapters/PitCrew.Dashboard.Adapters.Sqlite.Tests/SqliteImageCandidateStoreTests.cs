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
  private const string OriginMainMigration23Checksum =
      "094549A32A957BB0A69F805619F0134BBB168D9B02FE9D2003A7F6DA91310B2C";

  [Test]
  public async Task Latest_Migrations_Apply_And_Recipe_Disable_By_Guid_Is_Idempotent(
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
      var disabled = await context.Store.DisableRecipeRegistrationAsync(
          recipe.TenantId,
          recipe.RegistrationId,
          context.Owner.GitHubUserId,
          disabledAt,
          cancellationToken);
      var disableReplay = await context.Store.DisableRecipeRegistrationAsync(
          recipe.TenantId,
          recipe.RegistrationId,
          context.Owner.GitHubUserId,
          disabledAt,
          cancellationToken);
      var disableRewrite = await context.Store.DisableRecipeRegistrationAsync(
          recipe.TenantId,
          recipe.RegistrationId,
          context.Owner.GitHubUserId,
          disabledAt.AddMinutes(1),
          cancellationToken);
      var stored = await context.Store.GetRecipeRegistrationOrNullAsync(
          recipe.TenantId,
          recipe.RegistrationId,
          cancellationToken);
      var listedWithoutDisabled = await context.Store.ListRecipeRegistrationsAsync(
          recipe.TenantId,
          includeDisabled: false,
          10,
          cancellationToken);
      var listedWithDisabled = await context.Store.ListRecipeRegistrationsAsync(
          recipe.TenantId,
          includeDisabled: true,
          10,
          cancellationToken);

      await Assert.That(migrationVersion).IsEqualTo(27);
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
          .IsEqualTo(ImageCandidateMutationResult.Unchanged);
      await Assert.That(listedWithoutDisabled).IsEmpty();
      await Assert.That(listedWithDisabled).HasSingleItem();
      await Assert.That(stored!.DisabledAt).IsEqualTo(disabledAt);
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Migrations_25_Through_27_Upgrade_Exact_Migration_24_And_Preserve_Checksums(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("migration-25-upgrade");
    try
    {
      await Assert.That(SqliteMigrationCatalog.All
              .Single(static migration => migration.Version == 23).Checksum)
          .IsEqualTo(OriginMainMigration23Checksum);

      var factory = CreateFactory(databasePath);
      await SqliteMigrationTestDatabase.ApplyThroughAsync(
          factory,
          24,
          cancellationToken);
      var accessStore = new SqliteAccessStore(factory);
      var now = DateTimeOffset.Parse(
          "2026-08-23T13:00:00+00:00",
          CultureInfo.InvariantCulture);
      var owner = new DashboardUser(
          "owner-user",
          "owner-login",
          "Owner",
          null);
      await accessStore.EnsureTenantOwnerAsync(
          "tenant-a",
          "Tenant A",
          owner,
          now,
          cancellationToken);
      var registrationId = Guid.Parse(
          "aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb",
          CultureInfo.InvariantCulture);
      var requestId = Guid.Parse(
          "cccccccc-1111-2222-3333-dddddddddddd",
          CultureInfo.InvariantCulture);
      const string sourceRefPolicyJson =
          "{\"allowedSourceRefs\":[\"refs/heads/main\"]}";
      const string inputSchemaJson =
          "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{}}";
      const string emptyInputJson = "{}";
      await ExecuteNonQueryAsync(
          factory,
          $$"""
          INSERT INTO image_recipe_versions (
              tenant_id,
              registration_id,
              version,
              github_installation_id,
              github_repository_id,
              github_workflow_id,
              repository_owner,
              repository_name,
              workflow_path,
              workflow_blob_sha,
              dispatch_ref,
              recipe_id,
              candidate_schema_version,
              source_ref_policy_json,
              input_schema_json,
              created_by_github_user_id,
              created_at)
          VALUES (
              'tenant-a',
              '{{registrationId:D}}',
              1,
              1001,
              2001,
              3001,
              'ncosentino',
              'pitcrew-dashboard',
              '.github/workflows/image-candidate.yml',
              '{{new string('a', 40)}}',
              'release/v1',
              'pitcrew-default',
              1,
              '{{sourceRefPolicyJson}}',
              '{{inputSchemaJson}}',
              '{{owner.GitHubUserId}}',
              '{{now:O}}');

          INSERT INTO image_build_requests (
              request_id,
              tenant_id,
              registration_id,
              registration_version,
              recipe_id,
              source_repository,
              source_commit,
              input_values_json,
              input_values_sha256,
              requested_by_github_user_id,
              requested_at,
              status,
              updated_at)
          VALUES (
              '{{requestId:D}}',
              'tenant-a',
              '{{registrationId:D}}',
              1,
              'pitcrew-default',
              'ncosentino/pitcrew-dashboard',
              '{{new string('b', 40)}}',
              '{{emptyInputJson}}',
              '44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a',
              '{{owner.GitHubUserId}}',
              '{{now.AddMinutes(1):O}}',
              'requested',
              '{{now.AddMinutes(1):O}}');
          """,
          cancellationToken);
      var priorChecksums = await ReadMigrationChecksumsAsync(
          factory,
          cancellationToken);

      await new SqliteMigrationRunner(factory).ApplyAsync(cancellationToken);

      var store = new SqliteImageCandidateStore(factory);
      var afterChecksums = await ReadMigrationChecksumsAsync(
          factory,
          cancellationToken);
      var migratedRecipe = await store.GetRecipeVersionOrNullAsync(
          "tenant-a",
          registrationId,
          1,
          cancellationToken);
      var migratedRequest = await store.GetBuildRequestOrNullAsync(
          "tenant-a",
          requestId,
          cancellationToken);
      var foreignKeyIssues = await ExecuteScalarAsync<long>(
          factory,
          "SELECT COUNT(*) FROM pragma_foreign_key_check;",
          cancellationToken);
      var integrityCheck = await ExecuteScalarAsync<string>(
          factory,
          "PRAGMA integrity_check;",
          cancellationToken);
      await Assert.That(priorChecksums.Keys.Max()).IsEqualTo(24);
      await Assert.That(priorChecksums[23])
          .IsEqualTo(OriginMainMigration23Checksum);
      await Assert.That(afterChecksums.Keys.Max()).IsEqualTo(27);
      await Assert.That(afterChecksums[25])
          .IsEqualTo(SqliteMigrationCatalog.All
              .Single(static migration => migration.Version == 25).Checksum);
      await Assert.That(afterChecksums[26])
          .IsEqualTo(SqliteMigrationCatalog.All
              .Single(static migration => migration.Version == 26).Checksum);
      await Assert.That(afterChecksums[27])
          .IsEqualTo(SqliteMigrationCatalog.All
              .Single(static migration => migration.Version == 27).Checksum);
      await Assert.That(
              priorChecksums.All(pair =>
                  afterChecksums.TryGetValue(
                      pair.Key,
                      out var checksum) &&
                  checksum == pair.Value))
          .IsTrue()
          .Because("released migrations 1 through 24 must retain their accepted checksums.");
      await Assert.That(migratedRecipe).IsNotNull();
      await Assert.That(migratedRecipe!.RegistrationId)
          .IsEqualTo(registrationId);
      await Assert.That(migratedRequest).IsNotNull();
      await Assert.That(migratedRequest!.Status)
          .IsEqualTo(ImageBuildRequestStatus.Blocked);
      await Assert.That(migratedRequest.SourceRef).IsEqualTo(string.Empty);
      await Assert.That(migratedRequest.GitHubRunApiUrl).IsNull();
      await Assert.That(migratedRequest.TerminalCategory)
          .IsEqualTo("migration-source-ref-missing");
      await Assert.That(foreignKeyIssues).IsEqualTo(0L);
      await Assert.That(integrityCheck).IsEqualTo("ok");
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
      var wrongTenantRegistration = await context.Store.GetRecipeRegistrationOrNullAsync(
          "tenant-b",
          recipe.RegistrationId,
          cancellationToken);
      var wrongTenantDisable = await context.Store.DisableRecipeVersionAsync(
          "tenant-b",
          recipe.RegistrationId,
          recipe.Version,
          context.Owner.GitHubUserId,
          context.Now.AddMinutes(2),
          cancellationToken);
      var wrongTenantDisableByGuid = await context.Store.DisableRecipeRegistrationAsync(
          "tenant-b",
          recipe.RegistrationId,
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
      await Assert.That(wrongTenantRegistration).IsNull();
      await Assert.That(wrongTenantDisable)
          .IsEqualTo(ImageCandidateMutationResult.NotFound);
      await Assert.That(wrongTenantDisableByGuid)
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
  public async Task Recipe_Registration_List_Can_Include_Or_Exclude_Disabled(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("latest-recipes");
    try
    {
      var context = await CreateContextAsync(
          databasePath,
          cancellationToken);
      var active = CreateRecipe(
          context.Now,
          context.Owner.GitHubUserId);
      var secondActive = CreateRecipe(
          context.Now.AddMinutes(1),
          context.Owner.GitHubUserId) with
      {
        RegistrationId = Guid.NewGuid(),
        RecipeId = "pitcrew-second",
        GitHubWorkflowId = 3002,
      };
      var disabled = CreateRecipe(
          context.Now.AddMinutes(2),
          context.Owner.GitHubUserId) with
      {
        RegistrationId = Guid.NewGuid(),
        RecipeId = "pitcrew-disabled",
        GitHubWorkflowId = 3003,
      };
      await context.Store.CreateRecipeVersionAsync(
          active,
          cancellationToken);
      await context.Store.CreateRecipeVersionAsync(
          secondActive,
          cancellationToken);
      await context.Store.CreateRecipeVersionAsync(
          disabled,
          cancellationToken);
      await context.Store.DisableRecipeRegistrationAsync(
          disabled.TenantId,
          disabled.RegistrationId,
          context.Owner.GitHubUserId,
          context.Now.AddMinutes(3),
          cancellationToken);

      var activeOnly =
          await context.Store.ListRecipeRegistrationsAsync(
              active.TenantId,
              includeDisabled: false,
              10,
              cancellationToken);
      var includingDisabled =
          await context.Store.ListRecipeRegistrationsAsync(
              active.TenantId,
              includeDisabled: true,
              10,
              cancellationToken);
      var wrongTenant =
          await context.Store.ListRecipeRegistrationsAsync(
              "tenant-b",
              includeDisabled: true,
              10,
              cancellationToken);
      var exactDisabled = await context.Store.GetRecipeRegistrationOrNullAsync(
          active.TenantId,
          disabled.RegistrationId,
          cancellationToken);

      await Assert.That(activeOnly).Count().IsEqualTo(2);
      await Assert.That(activeOnly.All(
              static registration => registration.DisabledAt is null))
          .IsTrue()
          .Because("disabled registrations are excluded by default");
      await Assert.That(includingDisabled).Count().IsEqualTo(3);
      await Assert.That(includingDisabled[2].RegistrationId)
          .IsEqualTo(disabled.RegistrationId);
      await Assert.That(includingDisabled[2].DisabledAt).IsNotNull();
      await Assert.That(exactDisabled).IsNotNull();
      await Assert.That(exactDisabled!.DisabledAt).IsNotNull();
      await Assert.That(wrongTenant).IsEmpty();
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Registration_Version_List_Is_Bounded_And_Tenant_Scoped(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("registration-versions");
    try
    {
      var context = await CreateContextAsync(
          databasePath,
          cancellationToken);
      var recipe = CreateRecipe(
          context.Now,
          context.Owner.GitHubUserId);
      await context.Store.CreateRecipeVersionAsync(
          recipe,
          cancellationToken);
      await context.Store.CreateRecipeVersionAsync(
          recipe with
          {
            Version = 2,
            WorkflowBlobSha = new string('b', 40),
            CreatedAt = context.Now.AddMinutes(1),
          },
          cancellationToken);
      await context.Store.CreateRecipeVersionAsync(
          recipe with
          {
            Version = 3,
            WorkflowBlobSha = new string('c', 40),
            CreatedAt = context.Now.AddMinutes(2),
          },
          cancellationToken);

      var versions =
          await context.Store.ListRegistrationVersionsAsync(
              recipe.TenantId,
              recipe.RegistrationId,
              2,
              cancellationToken);
      var wrongTenant =
          await context.Store.ListRegistrationVersionsAsync(
              "tenant-b",
              recipe.RegistrationId,
              2,
              cancellationToken);

      await Assert.That(versions).Count().IsEqualTo(2);
      await Assert.That(versions[0].Version).IsEqualTo(3);
      await Assert.That(versions[1].Version).IsEqualTo(2);
      await Assert.That(wrongTenant).IsEmpty();
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

  [Test]
  public async Task Due_Claims_Are_Single_Owner_Lease_Bounded_And_Terminal_Safe(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("execution-claims");
    try
    {
      var context = await CreateContextAsync(
          databasePath,
          cancellationToken);
      var recipe = CreateRecipe(
          context.Now,
          context.Owner.GitHubUserId);
      await context.Store.CreateRecipeVersionAsync(recipe, cancellationToken);
      var request = CreateRequest(
          recipe,
          context.Now.AddMinutes(1)) with
      {
        SourceRef = "refs/heads/main",
      };
      await context.Store.CreateBuildRequestAsync(request, cancellationToken);
      var secondStore = new SqliteImageCandidateStore(context.Factory);
      var concurrentClaims = await Task.WhenAll(
          context.Store.ClaimDueBuildRequestsAsync(
              "worker-a",
              context.Now.AddMinutes(1),
              context.Now.AddMinutes(3),
              1,
              cancellationToken),
          secondStore.ClaimDueBuildRequestsAsync(
              "worker-b",
              context.Now.AddMinutes(1),
              context.Now.AddMinutes(3),
              1,
              cancellationToken));
      var claimed = concurrentClaims.SelectMany(
          static claims => claims).ToArray();
      var beforeExpiry = await secondStore.ClaimDueBuildRequestsAsync(
          "worker-c",
          context.Now.AddMinutes(2),
          context.Now.AddMinutes(4),
          1,
          cancellationToken);
      var afterExpiry = await secondStore.ClaimDueBuildRequestsAsync(
          "worker-c",
          context.Now.AddMinutes(3),
          context.Now.AddMinutes(5),
          1,
          cancellationToken);
      var terminalized = await secondStore.TerminalizeBuildRequestAsync(
          request.TenantId,
          request.RequestId,
          "worker-c",
          ImageBuildRequestStatus.Failed,
          "workflow-failure",
          "The trusted workflow reported failure.",
          "run-completed-failure",
          context.Now.AddMinutes(3),
          cancellationToken);
      var afterTerminal = await context.Store.ClaimDueBuildRequestsAsync(
          "worker-d",
          context.Now.AddMinutes(10),
          context.Now.AddMinutes(12),
          10,
          cancellationToken);

      await Assert.That(claimed).HasSingleItem();
      await Assert.That(claimed[0].Request.RequestId)
          .IsEqualTo(request.RequestId);
      await Assert.That(beforeExpiry).IsEmpty();
      await Assert.That(afterExpiry).HasSingleItem();
      await Assert.That(afterExpiry[0].LeaseOwner).IsEqualTo("worker-c");
      await Assert.That(terminalized)
          .IsEqualTo(ImageCandidateMutationResult.Succeeded);
      await Assert.That(afterTerminal).IsEmpty();
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Definitive_Dispatch_Rate_Limit_Survives_Reopen_And_Freezes_Run(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("dispatch-retry");
    try
    {
      var context = await CreateContextAsync(
          databasePath,
          cancellationToken);
      var recipe = CreateRecipe(
          context.Now,
          context.Owner.GitHubUserId);
      await context.Store.CreateRecipeVersionAsync(recipe, cancellationToken);
      var request = CreateRequest(
          recipe,
          context.Now.AddMinutes(1)) with
      {
        SourceRef = "refs/heads/main",
      };
      await context.Store.CreateBuildRequestAsync(request, cancellationToken);
      var firstClaim = (await context.Store.ClaimDueBuildRequestsAsync(
          "worker-a",
          context.Now.AddMinutes(1),
          context.Now.AddMinutes(3),
          1,
          cancellationToken)).Single();
      await context.Store.MarkDispatchStartedAsync(
          request.TenantId,
          request.RequestId,
          firstClaim.LeaseOwner,
          context.Now.AddMinutes(1),
          cancellationToken);
      var deferred = await context.Store.DeferRateLimitedDispatchAsync(
          request.TenantId,
          request.RequestId,
          firstClaim.LeaseOwner,
          context.Now.AddMinutes(5),
          "dispatch-rate-limited",
          context.Now.AddMinutes(1),
          cancellationToken);

      var reopened = new SqliteImageCandidateStore(context.Factory);
      var tooEarly = await reopened.ClaimDueBuildRequestsAsync(
          "worker-b",
          context.Now.AddMinutes(4),
          context.Now.AddMinutes(6),
          1,
          cancellationToken);
      var retryClaim = (await reopened.ClaimDueBuildRequestsAsync(
          "worker-b",
          context.Now.AddMinutes(5),
          context.Now.AddMinutes(7),
          1,
          cancellationToken)).Single();
      await reopened.MarkDispatchStartedAsync(
          request.TenantId,
          request.RequestId,
          retryClaim.LeaseOwner,
          context.Now.AddMinutes(5),
          cancellationToken);
      var succeeded = await reopened.RecordDispatchSucceededAsync(
          request.TenantId,
          request.RequestId,
          retryClaim.LeaseOwner,
          7001,
          "https://api.github.example/runs/7001",
          "https://github.example/runs/7001",
          context.Now.AddMinutes(6),
          context.Now.AddMinutes(5),
          cancellationToken);
      var stored = await reopened.GetBuildRequestOrNullAsync(
          request.TenantId,
          request.RequestId,
          cancellationToken);

      await Assert.That(deferred)
          .IsEqualTo(ImageCandidateMutationResult.Succeeded);
      await Assert.That(tooEarly).IsEmpty();
      await Assert.That(retryClaim.DispatchSafeToRetry).IsTrue()
          .Because("the definitive rate-limit disposition is durable");
      await Assert.That(succeeded)
          .IsEqualTo(ImageCandidateMutationResult.Succeeded);
      await Assert.That(stored!.Status)
          .IsEqualTo(ImageBuildRequestStatus.Building);
      await Assert.That(stored.GitHubRunId).IsEqualTo(7001);
      await Assert.That(stored.GitHubRunApiUrl)
          .IsEqualTo("https://api.github.example/runs/7001");
      await Assert.That(stored.GitHubRunUrl)
          .IsEqualTo("https://github.example/runs/7001");
    }
    finally
    {
      SqliteConnection.ClearAllPools();
      DashboardTestCleanup.DeleteDatabase(databasePath);
    }
  }

  [Test]
  public async Task Alternating_NotFound_Phases_Preserve_Independent_Budgets_Across_Reopen(
      CancellationToken cancellationToken)
  {
    var databasePath = CreateDatabasePath("independent-not-found-counters");
    try
    {
      var context = await CreateContextAsync(
          databasePath,
          cancellationToken);
      var recipe = CreateRecipe(
          context.Now,
          context.Owner.GitHubUserId);
      await context.Store.CreateRecipeVersionAsync(recipe, cancellationToken);
      var request = CreateRequest(
          recipe,
          context.Now.AddMinutes(1)) with
      {
        SourceRef = "refs/heads/main",
      };
      await context.Store.CreateBuildRequestAsync(request, cancellationToken);
      var cursor = context.Now.AddMinutes(1);
      var dispatchClaim =
          (await context.Store.ClaimDueBuildRequestsAsync(
              "dispatch-worker",
              cursor,
              cursor.AddMinutes(1),
              1,
              cancellationToken)).Single();
      await context.Store.MarkDispatchStartedAsync(
          request.TenantId,
          request.RequestId,
          dispatchClaim.LeaseOwner,
          cursor,
          cancellationToken);
      await context.Store.RecordDispatchSucceededAsync(
          request.TenantId,
          request.RequestId,
          dispatchClaim.LeaseOwner,
          7001,
          "https://api.github.example/runs/7001",
          "https://github.example/runs/7001",
          cursor.AddMinutes(1),
          cursor,
          cancellationToken);
      cursor = cursor.AddMinutes(1);

      for (var attempt = 0; attempt < 5; attempt++)
      {
        var runStore = new SqliteImageCandidateStore(context.Factory);
        var runClaim = (await runStore.ClaimDueBuildRequestsAsync(
            $"run-worker-{attempt}",
            cursor,
            cursor.AddMinutes(1),
            1,
            cancellationToken)).Single();
        await runStore.DeferBuildRunPollAsync(
            request.TenantId,
            request.RequestId,
            runClaim.LeaseOwner,
            cursor.AddMinutes(1),
            "run-not-found",
            ImageBuildNotFoundCounterAction.Increment,
            cursor,
            cancellationToken);
        cursor = cursor.AddMinutes(1);

        var revisionStore =
            new SqliteImageCandidateStore(context.Factory);
        var revisionClaim =
            (await revisionStore.ClaimDueBuildRequestsAsync(
                $"revision-worker-{attempt}",
                cursor,
                cursor.AddMinutes(1),
                1,
                cancellationToken)).Single();
        await revisionStore.DeferBuildRevisionPollAsync(
            request.TenantId,
            request.RequestId,
            revisionClaim.LeaseOwner,
            cursor.AddMinutes(1),
            "run-revision-not-found",
            ImageBuildNotFoundCounterAction.Increment,
            cursor,
            cancellationToken);
        cursor = cursor.AddMinutes(1);
      }

      var reopened = new SqliteImageCandidateStore(context.Factory);
      var exhausted = (await reopened.ClaimDueBuildRequestsAsync(
          "inspection-worker",
          cursor,
          cursor.AddMinutes(1),
          1,
          cancellationToken)).Single();

      await Assert.That(exhausted.RunNotFoundAttempts).IsEqualTo(5);
      await Assert.That(exhausted.RevisionNotFoundAttempts).IsEqualTo(5);
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
        requestedAt,
        "refs/heads/main");
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
      CreatePath(
          Path.Combine(
              GetRepositoryRoot(),
              "test-artifacts"),
          $"pitcrew-image-{scope}-{Guid.NewGuid():N}.db");

  private static string Sha256(string value) =>
      Convert.ToHexString(
          SHA256.HashData(Encoding.UTF8.GetBytes(value)))
      .ToLowerInvariant();

  private static string CreatePath(
      string directory,
      string fileName)
  {
    Directory.CreateDirectory(directory);
    return Path.Combine(
        directory,
        fileName);
  }

  private static string GetRepositoryRoot()
  {
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null &&
           !File.Exists(Path.Combine(
               current.FullName,
               "PitCrew.Dashboard.slnx")))
    {
      current = current.Parent;
    }

    return current?.FullName ??
        throw new InvalidOperationException(
            "Could not locate the repository root for SQLite image candidate tests.");
  }

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

  private static async Task<IReadOnlyDictionary<int, string>>
      ReadMigrationChecksumsAsync(
          SqliteConnectionFactory factory,
          CancellationToken cancellationToken)
  {
    await using var connection = await factory.OpenAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText =
        """
        SELECT version, checksum
        FROM schema_migrations
        ORDER BY version;
        """;
    var checksums = new Dictionary<int, string>();
    await using var reader = await command.ExecuteReaderAsync(
        cancellationToken);
    while (await reader.ReadAsync(cancellationToken))
    {
      checksums.Add(
          reader.GetInt32(0),
          reader.GetString(1));
    }
    return checksums;
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
