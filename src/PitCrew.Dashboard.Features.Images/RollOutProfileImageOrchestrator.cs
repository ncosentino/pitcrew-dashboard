using System.Security.Claims;

using PitCrew.Dashboard.Features.Images.Abstractions;
using PitCrew.Dashboard.Kernel.ImageRollouts;

namespace PitCrew.Dashboard.Features.Images;

/// <summary>
/// Loads one tenant-owned ready registry candidate, then hands off to the
/// shared kernel rollout queue.
/// </summary>
internal sealed class RollOutProfileImageOrchestrator(
    IImageCandidateStore _candidateStore,
    IRollOutProfileImageUnitOfWork _fleetUnitOfWork)
    : IRollOutProfileImageOrchestrator
{
  public async Task<RollOutProfileImageOutcome> QueueAsync(
      ClaimsPrincipal principal,
      string tenantId,
      RollOutProfileImageInput input,
      CancellationToken cancellationToken)
  {
    var validation = ValidateInput(input);
    if (validation is not null)
    {
      return new RollOutProfileImageOutcome(
          RollOutProfileImageStatus.Invalid,
          null,
          tenantId,
          "invalid_image_rollout_request",
          validation);
    }

    var fences = new ImageRolloutCommandFences(
        input.ExpectedCurrentImageReference,
        input.ExpectedCurrentImageDigest,
        input.ExpectedCurrentLocalImageId,
        input.ExpectedCurrentWorkerRevision,
        input.ExpectedStaticFingerprint,
        input.ExpectedPreservedConfigurationFingerprint,
        input.ExpectedRoutingFingerprint,
        input.ExpectedDesiredGeneration,
        input.ExpectedDesiredStateHash);

    // Probe before candidate lookup so retention cannot erase the answer
    // to an exact retry of an already durable command.
    var replayLookup = await _fleetUnitOfWork.LookupReplayOrNullAsync(
        principal,
        tenantId,
        input.NodeId,
        input.ProfileId,
        input.CandidateId,
        fences,
        input.IdempotencyKey,
        cancellationToken);
    if (replayLookup is null)
    {
      return new RollOutProfileImageOutcome(
          RollOutProfileImageStatus.Unauthorized,
          null,
          tenantId,
          "unauthorized_image_rollout",
          "The requesting principal is not a dashboard user.");
    }
    if (replayLookup.Outcome ==
        ImageRolloutIdempotencyLookupOutcome.IdempotentReplay)
    {
      return new RollOutProfileImageOutcome(
          RollOutProfileImageStatus.IdempotentReplay,
          replayLookup.CommandId,
          tenantId,
          null,
          null);
    }
    if (replayLookup.Outcome ==
        ImageRolloutIdempotencyLookupOutcome.IdempotencyKeyReuseConflict)
    {
      return new RollOutProfileImageOutcome(
          RollOutProfileImageStatus.IdempotencyKeyReuseConflict,
          null,
          tenantId,
          "image_rollout_idempotency_key_conflict",
          "The Idempotency-Key was already used with different rollout authority.");
    }

    var details = await _candidateStore.GetCandidateOrNullAsync(
        tenantId,
        input.CandidateId,
        cancellationToken);
    if (details is null)
    {
      return new RollOutProfileImageOutcome(
          RollOutProfileImageStatus.CandidateNotFound,
          null,
          tenantId,
          "image_candidate_not_found",
          "The requested candidate is not owned by this tenant.");
    }

    var ready = details.Candidate as ReadyImageCandidate;
    if (ready is null)
    {
      return new RollOutProfileImageOutcome(
          RollOutProfileImageStatus.CandidateFailed,
          null,
          tenantId,
          "image_candidate_not_ready",
          "The requested candidate is not eligible for rollout.");
    }
    if (ready.OutputMode != ImageCandidateOutputMode.Registry ||
        ready.ImmutableReference is null)
    {
      return new RollOutProfileImageOutcome(
          RollOutProfileImageStatus.CandidateNotRegistryReady,
          null,
          tenantId,
          "image_candidate_not_registry_ready",
          "The requested candidate has no immutable registry reference.");
    }
    if (!IsValidTargetDigest(ready.Digest))
    {
      return new RollOutProfileImageOutcome(
          RollOutProfileImageStatus.Invalid,
          null,
          tenantId,
          "invalid_image_rollout_request",
          "The candidate digest is invalid.");
    }

    var candidateAuthority = new ImageRolloutCandidateAuthority(
        ready.CandidateId,
        ready.RecipeId,
        ready.Digest,
        FormatPlatform(ready.Platform));

    var queueResult = await _fleetUnitOfWork.QueueOrNullAsync(
        principal,
        tenantId,
        input.NodeId,
        input.ProfileId,
        candidateAuthority,
        fences,
        input.IdempotencyKey,
        cancellationToken);
    if (queueResult is null)
    {
      return new RollOutProfileImageOutcome(
          RollOutProfileImageStatus.Unauthorized,
          null,
          tenantId,
          "unauthorized_image_rollout",
          "The requesting principal is not a dashboard user.");
    }

    return queueResult.Status switch
    {
      ImageRolloutCommandQueueStatus.Queued => new(
          RollOutProfileImageStatus.Queued,
          queueResult.CommandId,
          tenantId,
          null,
          null),
      ImageRolloutCommandQueueStatus.IdempotentReplay => new(
          RollOutProfileImageStatus.IdempotentReplay,
          queueResult.CommandId,
          tenantId,
          null,
          null),
      ImageRolloutCommandQueueStatus.IdempotencyKeyReuseConflict => new(
          RollOutProfileImageStatus.IdempotencyKeyReuseConflict,
          null,
          tenantId,
          "image_rollout_idempotency_key_conflict",
          "The Idempotency-Key was already used with different rollout authority."),
      ImageRolloutCommandQueueStatus.NodeNotFound => new(
          RollOutProfileImageStatus.CandidateNotFound,
          null,
          tenantId,
          "image_rollout_node_not_found",
          "The requested node was not found for this tenant."),
      ImageRolloutCommandQueueStatus.Unsupported => new(
          RollOutProfileImageStatus.Unsupported,
          null,
          tenantId,
          "image_rollout_unsupported",
          "The connector does not advertise image rollout for this profile."),
      ImageRolloutCommandQueueStatus.UnsupportedTopology => new(
          RollOutProfileImageStatus.UnsupportedTopology,
          null,
          tenantId,
          "image_rollout_unsupported_topology",
          "The connector cannot preserve the profile's routing state."),
      ImageRolloutCommandQueueStatus.NotAllowed => new(
          RollOutProfileImageStatus.NotAllowed,
          null,
          tenantId,
          "image_rollout_not_allowed",
          "Local connector policy does not allow this rollout."),
      ImageRolloutCommandQueueStatus.RecipeNotAllowed => new(
          RollOutProfileImageStatus.RecipeNotAllowed,
          null,
          tenantId,
          "image_rollout_recipe_not_allowed",
          "The candidate recipe is not on the profile's allowlist."),
      ImageRolloutCommandQueueStatus.RegistryNotAllowed => new(
          RollOutProfileImageStatus.RegistryNotAllowed,
          null,
          tenantId,
          "image_rollout_registry_not_allowed",
          "The connector's local registry policy does not allow this recipe."),
      ImageRolloutCommandQueueStatus.InvalidCandidate => new(
          RollOutProfileImageStatus.CandidateFailed,
          null,
          tenantId,
          "image_candidate_not_ready",
          "The candidate is not eligible for rollout."),
      ImageRolloutCommandQueueStatus.ArchitectureMismatch => new(
          RollOutProfileImageStatus.ArchitectureMismatch,
          null,
          tenantId,
          "image_rollout_architecture_mismatch",
          "The candidate architecture does not match the profile."),
      ImageRolloutCommandQueueStatus.StaleFence => new(
          RollOutProfileImageStatus.StaleFence,
          null,
          tenantId,
          "image_rollout_stale_fence",
          "The requested fences no longer match the connector's advertised state."),
      ImageRolloutCommandQueueStatus.Conflict => new(
          RollOutProfileImageStatus.Conflict,
          null,
          tenantId,
          "image_rollout_operation_active",
          "Another profile operation is already active."),
      ImageRolloutCommandQueueStatus.RateLimited => new(
          RollOutProfileImageStatus.RateLimited,
          null,
          tenantId,
          "image_rollout_cooldown",
          "A rollout for this profile was requested too recently."),
      _ => new(
          RollOutProfileImageStatus.Invalid,
          null,
          tenantId,
          "invalid_image_rollout_request",
          "Unsupported image rollout queue status."),
    };
  }

  private static string? ValidateInput(RollOutProfileImageInput input)
  {
    if (input.NodeId == Guid.Empty)
    {
      return "nodeId must be a non-empty GUID.";
    }
    if (!PitCrew.Protocol.PitCrewProfileId.IsValid(input.ProfileId))
    {
      return "profileId must match the pitcrew profile-id contract "
          + "(1-32 characters, starting with a lowercase letter and "
          + "containing only lowercase letters, digits, and '-').";
    }
    if (input.CandidateId == Guid.Empty)
    {
      return "candidateId must be a non-empty GUID.";
    }
    if (!IsValidIdempotencyKey(input.IdempotencyKey))
    {
      return "idempotencyKey must be 8 to 200 characters of "
          + "ASCII letters, digits, '-', '_', '.', or ':' "
          + "and cannot be blank.";
    }
    if (!IsValidHex(input.ExpectedStaticFingerprint, 64) ||
        !IsValidHex(input.ExpectedPreservedConfigurationFingerprint, 64) ||
        !IsValidHex(input.ExpectedRoutingFingerprint, 64))
    {
      return "expected fingerprints must be 64 lowercase hex characters.";
    }
    if (input.ExpectedDesiredStateHash is not null &&
        !IsValidHex(input.ExpectedDesiredStateHash, 64))
    {
      return "expectedDesiredStateHash must be 64 lowercase hex characters.";
    }
    if (input.ExpectedDesiredGeneration < 0)
    {
      return "expectedDesiredGeneration cannot be negative.";
    }
    if (!IsValidOptionalImageReference(input.ExpectedCurrentImageReference))
    {
      return "expectedCurrentImageReference must be 1 to 512 characters "
          + "and cannot contain whitespace, control characters, "
          + "quotes, or backslashes.";
    }
    if (input.ExpectedCurrentImageDigest is not null &&
        !IsValidDigest(input.ExpectedCurrentImageDigest))
    {
      return "expectedCurrentImageDigest must be a sha256 digest.";
    }
    if (input.ExpectedCurrentLocalImageId is not null &&
        !IsValidDigest(input.ExpectedCurrentLocalImageId))
    {
      return "expectedCurrentLocalImageId must be a sha256 digest.";
    }
    if (input.ExpectedCurrentWorkerRevision is not null &&
        !IsValidHex(input.ExpectedCurrentWorkerRevision, 64))
    {
      return "expectedCurrentWorkerRevision must be 64 lowercase hex characters.";
    }
    return null;
  }

  private static bool IsValidOptionalImageReference(string? reference)
  {
    if (reference is null)
    {
      return true;
    }
    if (reference.Length is < 1 or > 512)
    {
      return false;
    }
    foreach (var character in reference)
    {
      if (character is ' ' or '\t' or '\r' or '\n' or '\"' or '\\' ||
          char.IsControl(character))
      {
        return false;
      }
    }
    return true;
  }

  private static bool IsValidTargetDigest(string digest) =>
      digest is { Length: 71 } &&
      digest.StartsWith("sha256:", StringComparison.Ordinal) &&
      IsAllLowerHex(digest.AsSpan(7));

  internal static bool IsValidIdempotencyKey(string? key)
  {
    if (string.IsNullOrEmpty(key))
    {
      return false;
    }
    if (key.Length is < 8 or > 200)
    {
      return false;
    }
    foreach (var character in key)
    {
      var accepted = character is
          (>= 'A' and <= 'Z') or
          (>= 'a' and <= 'z') or
          (>= '0' and <= '9') or
          '-' or '_' or '.' or ':';
      if (!accepted)
      {
        return false;
      }
    }
    return true;
  }

  private static bool IsValidDigest(string value) =>
      value is { Length: 71 } &&
      value.StartsWith("sha256:", StringComparison.Ordinal) &&
      IsAllLowerHex(value.AsSpan(7));

  private static bool IsValidHex(string value, int length) =>
      value is not null &&
      value.Length == length &&
      IsAllLowerHex(value.AsSpan());

  private static bool IsAllLowerHex(ReadOnlySpan<char> value)
  {
    foreach (var character in value)
    {
      if (character is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
      {
        return false;
      }
    }
    return true;
  }

  private static string FormatPlatform(ImageCandidatePlatform platform) =>
      platform switch
      {
        ImageCandidatePlatform.LinuxAmd64 => "linux/amd64",
        ImageCandidatePlatform.LinuxArm64 => "linux/arm64",
        _ => throw new ArgumentOutOfRangeException(nameof(platform)),
      };
}
