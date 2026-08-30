using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using PitCrew.Dashboard.Kernel.ImageRollouts;

namespace PitCrew.Dashboard.Features.Fleet;

internal static class ImageRolloutCampaignSignatures
{
  public static string CreateForward(
      string tenantId,
      string actorGitHubUserId,
      Guid candidateId) =>
      Compute(
          ("action", "create-forward"),
          ("tenant", tenantId),
          ("actor", actorGitHubUserId),
          ("candidate", candidateId.ToString(
              "D",
              CultureInfo.InvariantCulture)));

  public static string CreateRollback(
      string tenantId,
      string actorGitHubUserId,
      Guid sourceCampaignId) =>
      Compute(
          ("action", "create-rollback"),
          ("tenant", tenantId),
          ("actor", actorGitHubUserId),
          ("source", sourceCampaignId.ToString(
              "D",
              CultureInfo.InvariantCulture)));

  public static string Configure(
      string tenantId,
      string actorGitHubUserId,
      Guid campaignId,
      ImageRolloutCampaignConfiguration configuration) =>
      Compute(
          ("action", "configure"),
          ("tenant", tenantId),
          ("actor", actorGitHubUserId),
          ("campaign", campaignId.ToString(
              "D",
              CultureInfo.InvariantCulture)),
          ("canary", configuration.CanaryTargetId?.ToString(
              "D",
              CultureInfo.InvariantCulture)),
          ("wave-size", configuration.WaveSize.ToString(
              CultureInfo.InvariantCulture)),
          ("revision", configuration.ExpectedRevision.ToString(
              CultureInfo.InvariantCulture)),
          ("target-set", configuration.ExpectedTargetSetHash));

  public static string ApproveWave(
      string tenantId,
      string actorGitHubUserId,
      Guid campaignId,
      ImageRolloutCampaignWaveApproval approval) =>
      Compute(
          ("action", "approve-wave"),
          ("tenant", tenantId),
          ("actor", actorGitHubUserId),
          ("campaign", campaignId.ToString(
              "D",
              CultureInfo.InvariantCulture)),
          ("wave", approval.WaveNumber.ToString(
              CultureInfo.InvariantCulture)),
          ("revision", approval.ExpectedRevision.ToString(
              CultureInfo.InvariantCulture)),
          ("target-set", approval.ExpectedTargetSetHash));

  public static string ChangeState(
      string action,
      string tenantId,
      string actorGitHubUserId,
      Guid campaignId,
      ImageRolloutCampaignMutationFence fence) =>
      Compute(
          ("action", action),
          ("tenant", tenantId),
          ("actor", actorGitHubUserId),
          ("campaign", campaignId.ToString(
              "D",
              CultureInfo.InvariantCulture)),
          ("revision", fence.ExpectedRevision.ToString(
              CultureInfo.InvariantCulture)),
          ("target-set", fence.ExpectedTargetSetHash));

  public static string ComputeTargetSetHash(
      ImageRolloutCampaignKind kind,
      Guid? sourceCampaignId,
      ImageRolloutCandidateAuthority? candidate,
      IReadOnlyList<ImageRolloutCampaignPlannedTarget> targets)
  {
    var builder = new StringBuilder();
    Append(builder, "kind", kind.ToString());
    Append(
        builder,
        "source",
        sourceCampaignId?.ToString("D", CultureInfo.InvariantCulture));
    AppendCandidate(builder, "campaign", candidate);
    foreach (var target in targets
        .OrderBy(static target => target.NodeId)
        .ThenBy(static target => target.ProfileId, StringComparer.Ordinal))
    {
      Append(
          builder,
          "node",
          target.NodeId.ToString("D", CultureInfo.InvariantCulture));
      Append(builder, "profile", target.ProfileId);
      Append(builder, "excluded", target.ExclusionCategory);
      AppendCandidate(builder, "target", target.Candidate);
      AppendFences(builder, target.Fences);
    }
    return Hash(builder);
  }

  private static string Compute(
      params (string Field, string? Value)[] values)
  {
    var builder = new StringBuilder();
    foreach (var (field, value) in values)
    {
      Append(builder, field, value);
    }
    return Hash(builder);
  }

  private static string Hash(StringBuilder builder) =>
      Convert.ToHexStringLower(
          SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));

  private static void AppendCandidate(
      StringBuilder builder,
      string prefix,
      ImageRolloutCandidateAuthority? candidate)
  {
    Append(
        builder,
        prefix + "-candidate",
        candidate?.CandidateId.ToString("D", CultureInfo.InvariantCulture));
    Append(builder, prefix + "-recipe", candidate?.RecipeId);
    Append(builder, prefix + "-digest", candidate?.TargetDigest);
    Append(builder, prefix + "-platform", candidate?.TargetPlatform);
  }

  private static void AppendFences(
      StringBuilder builder,
      ImageRolloutCommandFences? fences)
  {
    Append(builder, "current-ref", fences?.ExpectedCurrentImageReference);
    Append(builder, "current-digest", fences?.ExpectedCurrentImageDigest);
    Append(builder, "current-local", fences?.ExpectedCurrentLocalImageId);
    Append(builder, "current-worker", fences?.ExpectedCurrentWorkerRevision);
    Append(builder, "static", fences?.ExpectedStaticFingerprint);
    Append(
        builder,
        "preserved",
        fences?.ExpectedPreservedConfigurationFingerprint);
    Append(builder, "routing", fences?.ExpectedRoutingFingerprint);
    Append(
        builder,
        "generation",
        fences?.ExpectedDesiredGeneration.ToString(
            CultureInfo.InvariantCulture));
    Append(builder, "desired-hash", fences?.ExpectedDesiredStateHash);
  }

  private static void Append(
      StringBuilder builder,
      string field,
      string? value)
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
