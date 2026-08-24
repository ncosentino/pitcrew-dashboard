using System.ComponentModel.DataAnnotations;

using NexusLabs.Needlr.Generators;

namespace PitCrew.Dashboard.Features.Images;

/// <summary>
/// Configures bounded restart-safe trusted image build execution.
/// </summary>
[Options("PitCrew:Images:Execution", ValidateOnStart = true)]
public sealed class ImageBuildExecutionOptions
{
  [Range(1, 100)]
  public int BatchSize { get; set; } = 10;

  [Range(5, 3600)]
  public int PollIntervalSeconds { get; set; } = 15;

  [Range(300, 3600)]
  public int ClaimLeaseSeconds { get; set; } = 300;

  [Range(5, 3600)]
  public int RetryBackoffSeconds { get; set; } = 30;

  [Range(5, 3600)]
  public int MaximumRetryBackoffSeconds { get; set; } = 300;

  [Range(1, 100)]
  public int NotFoundMaximumAttempts { get; set; } = 5;

  [Range(30, 86400)]
  public int NotFoundGraceSeconds { get; set; } = 300;

  public IEnumerable<ValidationError> Validate()
  {
    if (ClaimLeaseSeconds <= PollIntervalSeconds)
    {
      yield return
          "ClaimLeaseSeconds must be greater than PollIntervalSeconds.";
    }
    if (MaximumRetryBackoffSeconds < RetryBackoffSeconds)
    {
      yield return
          "MaximumRetryBackoffSeconds must be at least RetryBackoffSeconds.";
    }
  }
}
