using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Options;

using PitCrew.Dashboard.Features.Fleet.Abstractions;
using PitCrew.Dashboard.Kernel.Authentication;
using PitCrew.Dashboard.Kernel.ImageRollouts;

namespace PitCrew.Dashboard.Features.Fleet;

internal sealed class RollOutProfileImageUnitOfWork(
    IImageRolloutCommandStore _commandStore,
    IAuthenticatedDashboardUserAccessor _userAccessor,
    IOptions<FleetDashboardOptions> _options,
    TimeProvider _timeProvider) : IRollOutProfileImageUnitOfWork
{
  public Task<ImageRolloutCommandQueueResult?> QueueOrNullAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid nodeId,
      string profileId,
      ImageRolloutCandidateAuthority candidate,
      ImageRolloutCommandFences fences,
      string idempotencyKey,
      CancellationToken cancellationToken)
  {
    var user = _userAccessor.GetOrNull(principal);
    if (user is null)
    {
      return Task.FromResult<ImageRolloutCommandQueueResult?>(null);
    }

    var options = _options.Value;
    var requestedAt = _timeProvider.GetUtcNow();
    var signature = ComputeSignature(
        tenantId,
        user.GitHubUserId,
        nodeId,
        profileId,
        candidate.CandidateId,
        fences);
    return QueueAsync();

    async Task<ImageRolloutCommandQueueResult?> QueueAsync() =>
        await _commandStore.QueueAsync(
            tenantId,
            nodeId,
            profileId,
            candidate,
            fences,
            user.GitHubUserId,
            idempotencyKey,
            signature,
            requestedAt,
            requestedAt.AddMinutes(options.ImageRolloutCommandLifetimeMinutes),
            requestedAt.AddSeconds(
                -options.ImageRolloutCapabilityFreshnessSeconds),
            requestedAt.AddSeconds(
                -options.ImageRolloutCommandCooldownSeconds),
            cancellationToken);
  }

  public Task<ImageRolloutIdempotencyLookup?> LookupReplayOrNullAsync(
      ClaimsPrincipal principal,
      string tenantId,
      Guid nodeId,
      string profileId,
      Guid candidateId,
      ImageRolloutCommandFences fences,
      string idempotencyKey,
      CancellationToken cancellationToken)
  {
    var user = _userAccessor.GetOrNull(principal);
    if (user is null)
    {
      return Task.FromResult<ImageRolloutIdempotencyLookup?>(null);
    }

    var signature = ComputeSignature(
        tenantId,
        user.GitHubUserId,
        nodeId,
        profileId,
        candidateId,
        fences);
    return LookupAsync();

    async Task<ImageRolloutIdempotencyLookup?> LookupAsync() =>
        await _commandStore.LookupIdempotentReplayAsync(
            tenantId,
            nodeId,
            user.GitHubUserId,
            idempotencyKey,
            signature,
            cancellationToken);
  }

  // Candidate-derived recipe, digest, and platform are omitted so the same
  // signature remains computable before candidate lookup and after retention.
  internal static string ComputeSignature(
      string tenantId,
      string actorGitHubUserId,
      Guid nodeId,
      string profileId,
      Guid candidateId,
      ImageRolloutCommandFences fences)
  {
    var builder = new StringBuilder();
    Append(builder, "tenant", tenantId);
    Append(builder, "actor", actorGitHubUserId);
    Append(builder, "node", nodeId.ToString("D", CultureInfo.InvariantCulture));
    Append(builder, "profile", profileId);
    Append(builder, "candidate", candidateId.ToString(
        "D",
        CultureInfo.InvariantCulture));
    Append(builder, "static", fences.ExpectedStaticFingerprint);
    Append(builder, "preserved", fences.ExpectedPreservedConfigurationFingerprint);
    Append(builder, "routing", fences.ExpectedRoutingFingerprint);
    Append(
        builder,
        "generation",
        fences.ExpectedDesiredGeneration.ToString(CultureInfo.InvariantCulture));
    Append(builder, "desired-hash", fences.ExpectedDesiredStateHash);
    Append(builder, "current-ref", fences.ExpectedCurrentImageReference);
    Append(builder, "current-digest", fences.ExpectedCurrentImageDigest);
    Append(builder, "current-local", fences.ExpectedCurrentLocalImageId);
    Append(builder, "current-worker", fences.ExpectedCurrentWorkerRevision);
    return Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
  }

  private static void Append(StringBuilder builder, string field, string? value)
  {
    builder.Append(field);
    builder.Append('\u0001');
    if (value is not null)
    {
      builder.Append(value);
    }
    builder.Append('\u001e');
  }
}
