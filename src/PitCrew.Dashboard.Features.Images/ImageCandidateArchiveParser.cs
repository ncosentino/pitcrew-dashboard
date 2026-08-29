using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using PitCrew.Dashboard.Features.Images.Abstractions;

namespace PitCrew.Dashboard.Features.Images;

internal static partial class ImageCandidateArchiveParser
{
  internal const string ArtifactName = "pitcrew-image-candidate";
  internal const string ReportName = "image-candidate.json";

  private static readonly UTF8Encoding _strictUtf8 =
      new(
          encoderShouldEmitUTF8Identifier: false,
          throwOnInvalidBytes: true);
  private static readonly FrozenSet<string> _rootProperties =
      new[]
      {
        "schemaVersion",
        "status",
        "recipeId",
        "createdAt",
        "source",
        "image",
        "qualifications",
        "failureCategory",
        "failureDetail",
      }.ToFrozenSet(StringComparer.Ordinal);
  private static readonly FrozenSet<string> _sourceProperties =
      new[]
      {
        "repository",
        "commit",
        "workflowRunId",
      }.ToFrozenSet(StringComparer.Ordinal);
  private static readonly FrozenSet<string> _imageProperties =
      new[]
      {
        "reference",
        "digest",
        "immutableReference",
        "platform",
        "outputMode",
      }.ToFrozenSet(StringComparer.Ordinal);
  private static readonly FrozenSet<string> _qualificationProperties =
      new[]
      {
        "name",
        "status",
      }.ToFrozenSet(StringComparer.Ordinal);
  private static readonly FrozenDictionary<string, string> _failureDetails =
      new Dictionary<string, string>(StringComparer.Ordinal)
      {
        ["build-failed"] = "Image build did not complete.",
        ["digest-unavailable"] =
            "BuildKit did not return an immutable image digest.",
        ["registry-verification-failed"] =
            "Registry digest verification failed.",
        ["registry-digest-mismatch"] =
            "Registry digest did not match BuildKit digest.",
        ["oci-verification-failed"] =
            "OCI output verification failed.",
        ["oci-digest-mismatch"] =
            "OCI output digest did not match BuildKit digest.",
        ["oci-manifest-missing"] =
            "OCI output omitted its declared manifest blob.",
        ["builder-cleanup-failed"] =
            "BuildKit cleanup did not reach an empty state.",
      }.ToFrozenDictionary(StringComparer.Ordinal);

  public static ImageCandidateArchiveParseOutcome Parse(
      ImageBuildExecutionClaim claim,
      GitHubWorkflowArtifact artifact,
      GitHubWorkflowArtifactArchive archive,
      int maximumArchiveBytes,
      int maximumReportBytes)
  {
    if (!HasExpectedAuthority(claim, artifact, archive) ||
        maximumArchiveBytes is <= 0 ||
        maximumReportBytes is <= 0 ||
        maximumReportBytes > maximumArchiveBytes ||
        archive.Content.Length is <= 0 ||
        archive.Content.Length > maximumArchiveBytes)
    {
      return Invalid(
          "candidate-evidence-identity-invalid",
          "Candidate evidence does not match the frozen request authority.");
    }

    var artifactDigest =
        $"sha256:{Convert.ToHexStringLower(SHA256.HashData(archive.Content.Span))}";
    if (!string.Equals(
            artifact.Digest,
            artifactDigest,
            StringComparison.Ordinal))
    {
      return Invalid(
          "candidate-artifact-digest-mismatch",
          "The candidate artifact archive digest does not match GitHub metadata.");
    }

    if (!HasSupportedZipLayout(archive.Content.Span))
    {
      return Invalid(
          "candidate-archive-unsupported",
          "The candidate artifact archive uses an unsupported ZIP layout or compression.");
    }

    var reportBytes = ArrayPool<byte>.Shared.Rent(maximumReportBytes + 1);
    try
    {
      var reportLength = ReadReport(
          archive.Content,
          reportBytes,
          maximumReportBytes);
      if (reportLength <= 0)
      {
        return Invalid(
            "candidate-archive-invalid",
            "The candidate artifact must contain exactly one regular image-candidate.json file.");
      }

      return ParseReport(
          claim,
          artifact,
          artifactDigest,
          reportBytes.AsSpan(0, reportLength));
    }
    catch (InvalidDataException)
    {
      return Invalid(
          "candidate-archive-invalid",
          "The candidate artifact ZIP could not be read safely.");
    }
    catch (IOException)
    {
      return Invalid(
          "candidate-archive-invalid",
          "The candidate artifact ZIP could not be read safely.");
    }
    finally
    {
      CryptographicOperations.ZeroMemory(reportBytes);
      ArrayPool<byte>.Shared.Return(reportBytes);
    }
  }

  private static int ReadReport(
      ReadOnlyMemory<byte> archive,
      byte[] destination,
      int maximumReportBytes)
  {
    using var archiveStream = new MemoryStream(
        archive.ToArray(),
        writable: false);
    using var zip = new ZipArchive(
        archiveStream,
        ZipArchiveMode.Read,
        leaveOpen: false,
        entryNameEncoding: _strictUtf8);
    if (zip.Entries.Count != 1)
    {
      return 0;
    }

    var entry = zip.Entries[0];
    var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
    var attributes = (FileAttributes)(entry.ExternalAttributes & 0xFFFF);
    if (!string.Equals(entry.FullName, ReportName, StringComparison.Ordinal) ||
        !string.Equals(entry.Name, ReportName, StringComparison.Ordinal) ||
        unixType is not 0 and not 0x8000 ||
        (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0 ||
        entry.Length is <= 0 ||
        entry.Length > maximumReportBytes ||
        entry.CompressedLength is <= 0 ||
        entry.CompressedLength > archive.Length)
    {
      return 0;
    }

    using var entryStream = entry.Open();
    var total = 0;
    while (total < maximumReportBytes + 1)
    {
      var read = entryStream.Read(
          destination.AsSpan(
              total,
              maximumReportBytes + 1 - total));
      if (read == 0)
      {
        break;
      }
      total += read;
    }

    return total == entry.Length &&
        total <= maximumReportBytes &&
        entryStream.ReadByte() == -1
        ? total
        : 0;
  }

  private static ImageCandidateArchiveParseOutcome ParseReport(
      ImageBuildExecutionClaim claim,
      GitHubWorkflowArtifact artifact,
      string artifactDigest,
      ReadOnlySpan<byte> reportBytes)
  {
    string reportJson;
    try
    {
      reportJson = _strictUtf8.GetString(reportBytes);
    }
    catch (DecoderFallbackException)
    {
      return Invalid(
          "candidate-report-utf8-invalid",
          "The candidate report is not valid UTF-8.");
    }

    try
    {
      using var document = JsonDocument.Parse(
          reportBytes.ToArray(),
          new JsonDocumentOptions
          {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 8,
          });
      return ParseRoot(
          claim,
          artifact,
          artifactDigest,
          reportJson,
          reportBytes,
          document.RootElement);
    }
    catch (JsonException)
    {
      return Invalid(
          "candidate-report-json-invalid",
          "The candidate report is not valid bounded JSON.");
    }
  }

  private static ImageCandidateArchiveParseOutcome ParseRoot(
      ImageBuildExecutionClaim claim,
      GitHubWorkflowArtifact artifact,
      string artifactDigest,
      string reportJson,
      ReadOnlySpan<byte> reportBytes,
      JsonElement root)
  {
    if (!HasExactProperties(root, _rootProperties) ||
        !root.TryGetProperty("schemaVersion", out var schemaVersion) ||
        schemaVersion.ValueKind != JsonValueKind.Number ||
        !schemaVersion.TryGetInt32(out var version))
    {
      return InvalidReport();
    }
    if (version != 1 ||
        claim.Registration.CandidateSchemaVersion != version)
    {
      return Invalid(
          "candidate-report-schema-unsupported",
          "The candidate report schema version is unsupported.");
    }
    if (!GetString(root, "status", out var status) ||
        status is not "ready" and not "failed" ||
        !GetString(root, "recipeId", out var recipeId) ||
        !Matches(RecipeIdRegex(), recipeId) ||
        !string.Equals(
            recipeId,
            claim.Request.RecipeId,
            StringComparison.Ordinal) ||
        !GetDateTimeOffset(root, "createdAt", out var createdAt) ||
        !root.TryGetProperty("source", out var source) ||
        !ReadSource(claim, source) ||
        !root.TryGetProperty("image", out var image) ||
        !ReadImage(
            image,
            out var imageReference,
            out var digest,
            out var immutableReference,
            out var platform,
            out var outputMode) ||
        !root.TryGetProperty("qualifications", out var qualificationsElement) ||
        !ReadQualifications(
            qualificationsElement,
            outputMode,
            out var qualificationValues))
    {
      return InvalidReport();
    }

    var failureCategory = ReadNullableString(root, "failureCategory");
    var failureDetail = ReadNullableString(root, "failureDetail");
    if (failureCategory.Invalid || failureDetail.Invalid ||
        !HasValidOutcome(
            status,
            imageReference,
            digest,
            immutableReference,
            outputMode,
            qualificationValues,
            failureCategory.Value,
            failureDetail.Value))
    {
      return InvalidReport();
    }

    var candidateId = claim.Request.RequestId;
    var qualifications = qualificationValues
        .Select(value => new ImageCandidateQualification(
            candidateId,
            value.Key,
            value.Value))
        .OrderBy(value => value.Name)
        .ToArray();
    var reportHash = Convert.ToHexStringLower(SHA256.HashData(reportBytes));
    ImageCandidate candidate = status == "ready"
        ? new ReadyImageCandidate(
            candidateId,
            claim.Request.TenantId,
            claim.Request.RequestId,
            recipeId,
            claim.Request.SourceRepository,
            claim.Request.SourceCommit,
            claim.Request.GitHubRunId!.Value,
            artifact.Id,
            artifact.Name,
            artifactDigest,
            reportHash,
            reportJson,
            imageReference,
            platform,
            outputMode,
            createdAt,
            claim.Request.UpdatedAt,
            digest!,
            immutableReference)
        : new FailedImageCandidate(
            candidateId,
            claim.Request.TenantId,
            claim.Request.RequestId,
            recipeId,
            claim.Request.SourceRepository,
            claim.Request.SourceCommit,
            claim.Request.GitHubRunId!.Value,
            artifact.Id,
            artifact.Name,
            artifactDigest,
            reportHash,
            reportJson,
            imageReference,
            platform,
            outputMode,
            createdAt,
            claim.Request.UpdatedAt,
            digest,
            immutableReference,
            failureCategory.Value!,
            failureDetail.Value!);
    return ImageCandidateArchiveParseOutcome.Success(
        candidate,
        qualifications);
  }

  private static bool ReadSource(
      ImageBuildExecutionClaim claim,
      JsonElement source)
  {
    if (!HasExactProperties(source, _sourceProperties) ||
        !GetString(source, "repository", out var repository) ||
        !GetString(source, "commit", out var commit) ||
        !source.TryGetProperty("workflowRunId", out var runId) ||
        runId.ValueKind != JsonValueKind.Number ||
        !runId.TryGetInt64(out var workflowRunId))
    {
      return false;
    }

    return string.Equals(
            repository,
            claim.Request.SourceRepository,
            StringComparison.Ordinal) &&
        string.Equals(
            commit,
            claim.Request.SourceCommit,
            StringComparison.Ordinal) &&
        workflowRunId == claim.Request.GitHubRunId;
  }

  private static bool ReadImage(
      JsonElement image,
      out string imageReference,
      out string? digest,
      out string? immutableReference,
      out ImageCandidatePlatform platform,
      out ImageCandidateOutputMode outputMode)
  {
    imageReference = string.Empty;
    digest = null;
    immutableReference = null;
    platform = default;
    outputMode = default;
    if (!HasExactProperties(image, _imageProperties) ||
        !GetString(image, "reference", out imageReference) ||
        imageReference.Length > 512 ||
        !Matches(ImageReferenceRegex(), imageReference))
    {
      return false;
    }

    var digestValue = ReadNullableString(image, "digest");
    var immutableValue = ReadNullableString(image, "immutableReference");
    if (digestValue.Invalid ||
        immutableValue.Invalid ||
        digestValue.Value is not null &&
        !Matches(DigestRegex(), digestValue.Value) ||
        immutableValue.Value is not null &&
        (immutableValue.Value.Length > 584 ||
         !Matches(ImmutableReferenceRegex(), immutableValue.Value)) ||
        !GetString(image, "platform", out var platformValue) ||
        !GetString(image, "outputMode", out var outputModeValue))
    {
      return false;
    }

    digest = digestValue.Value;
    immutableReference = immutableValue.Value;
    platform = platformValue switch
    {
      "linux/amd64" => ImageCandidatePlatform.LinuxAmd64,
      "linux/arm64" => ImageCandidatePlatform.LinuxArm64,
      _ => (ImageCandidatePlatform)(-1),
    };
    outputMode = outputModeValue switch
    {
      "registry" => ImageCandidateOutputMode.Registry,
      "oci" => ImageCandidateOutputMode.Oci,
      _ => (ImageCandidateOutputMode)(-1),
    };
    return Enum.IsDefined(platform) && Enum.IsDefined(outputMode);
  }

  private static bool ReadQualifications(
      JsonElement element,
      ImageCandidateOutputMode outputMode,
      out IReadOnlyDictionary<ImageCandidateQualificationName,
          ImageCandidateQualificationStatus> qualifications)
  {
    qualifications = ReadOnlyDictionary<
        ImageCandidateQualificationName,
        ImageCandidateQualificationStatus>.Empty;
    if (element.ValueKind != JsonValueKind.Array ||
        element.GetArrayLength() != 4)
    {
      return false;
    }

    var values =
        new Dictionary<ImageCandidateQualificationName,
            ImageCandidateQualificationStatus>();
    using var items = element.EnumerateArray();
    while (items.MoveNext())
    {
      var item = items.Current;
      if (!HasExactProperties(item, _qualificationProperties) ||
          !GetString(item, "name", out var nameValue) ||
          !GetString(item, "status", out var statusValue) ||
          !MapQualificationName(nameValue, out var name) ||
          !MapQualificationStatus(statusValue, out var status) ||
          !values.TryAdd(name, status))
      {
        return false;
      }
    }

    var outputName = outputMode == ImageCandidateOutputMode.Registry
        ? ImageCandidateQualificationName.RegistryDigest
        : ImageCandidateQualificationName.OciManifest;
    if (!values.ContainsKey(ImageCandidateQualificationName.ImageBuild) ||
        !values.ContainsKey(ImageCandidateQualificationName.BuildKitDigest) ||
        !values.ContainsKey(outputName) ||
        !values.ContainsKey(ImageCandidateQualificationName.BuilderCleanup))
    {
      return false;
    }

    qualifications = values;
    return true;
  }

  private static bool HasValidOutcome(
      string status,
      string imageReference,
      string? digest,
      string? immutableReference,
      ImageCandidateOutputMode outputMode,
      IReadOnlyDictionary<ImageCandidateQualificationName,
          ImageCandidateQualificationStatus> qualifications,
      string? failureCategory,
      string? failureDetail)
  {
    if (outputMode == ImageCandidateOutputMode.Oci &&
        immutableReference is not null)
    {
      return false;
    }
    if (immutableReference is not null &&
        (digest is null ||
         !string.Equals(
             immutableReference,
             ExpectedImmutableReference(imageReference, digest),
             StringComparison.Ordinal)))
    {
      return false;
    }

    if (status == "ready")
    {
      return digest is not null &&
          failureCategory is null &&
          failureDetail is null &&
          qualifications.Values.All(value =>
              value == ImageCandidateQualificationStatus.Passed) &&
          (outputMode == ImageCandidateOutputMode.Oci ||
           immutableReference is not null);
    }

    return failureCategory is not null &&
        failureDetail is not null &&
        _failureDetails.TryGetValue(
            failureCategory,
            out var expectedDetail) &&
        string.Equals(
            failureDetail,
            expectedDetail,
            StringComparison.Ordinal) &&
        qualifications.Values.Any(value =>
            value != ImageCandidateQualificationStatus.Passed);
  }

  private static bool HasExpectedAuthority(
      ImageBuildExecutionClaim claim,
      GitHubWorkflowArtifact artifact,
      GitHubWorkflowArtifactArchive archive)
  {
    var canonicalRepository =
        $"{claim.Registration.RepositoryOwner}/{claim.Registration.RepositoryName}";
    return claim.Request.Status == ImageBuildRequestStatus.Qualifying &&
        claim.Request.GitHubRunId is > 0 &&
        claim.Request.RegistrationId == claim.Registration.RegistrationId &&
        claim.Request.RegistrationVersion == claim.Registration.Version &&
        string.Equals(
            claim.Request.TenantId,
            claim.Registration.TenantId,
            StringComparison.Ordinal) &&
        string.Equals(
            claim.Request.RecipeId,
            claim.Registration.RecipeId,
            StringComparison.Ordinal) &&
        string.Equals(
            claim.Request.SourceRepository,
            canonicalRepository,
            StringComparison.Ordinal) &&
        artifact.Id == archive.ArtifactId &&
        artifact.WorkflowRunId == claim.Request.GitHubRunId &&
        string.Equals(artifact.Name, ArtifactName, StringComparison.Ordinal) &&
        artifact.Digest is not null;
  }

  private static int FindEndOfCentralDirectory(ReadOnlySpan<byte> archive)
  {
    const uint signature = 0x06054B50;
    const int minimumRecordBytes = 22;
    const int maximumCommentBytes = ushort.MaxValue;
    var minimum = Math.Max(
        0,
        archive.Length - minimumRecordBytes - maximumCommentBytes);
    for (var offset = archive.Length - minimumRecordBytes;
         offset >= minimum;
         offset--)
    {
      if (BinaryPrimitives.ReadUInt32LittleEndian(archive[offset..]) ==
          signature)
      {
        return offset;
      }
    }
    return -1;
  }

  private static bool HasSupportedZipLayout(ReadOnlySpan<byte> archive)
  {
    const uint localHeaderSignature = 0x04034B50;
    const uint centralHeaderSignature = 0x02014B50;
    const int localHeaderBytes = 30;
    const int centralHeaderBytes = 46;
    if (archive.Length < localHeaderBytes + centralHeaderBytes + 22 ||
        BinaryPrimitives.ReadUInt32LittleEndian(archive) !=
            localHeaderSignature)
    {
      return false;
    }

    var endOffset = FindEndOfCentralDirectory(archive);
    var commentLength = endOffset < 0
        ? 0
        : BinaryPrimitives.ReadUInt16LittleEndian(
            archive[(endOffset + 20)..]);
    if (endOffset < 0 ||
        endOffset + 22 + commentLength != archive.Length ||
        BinaryPrimitives.ReadUInt16LittleEndian(archive[(endOffset + 4)..]) != 0 ||
        BinaryPrimitives.ReadUInt16LittleEndian(archive[(endOffset + 6)..]) != 0 ||
        BinaryPrimitives.ReadUInt16LittleEndian(archive[(endOffset + 8)..]) != 1 ||
        BinaryPrimitives.ReadUInt16LittleEndian(archive[(endOffset + 10)..]) != 1)
    {
      return false;
    }

    var centralOffset = BinaryPrimitives.ReadUInt32LittleEndian(
        archive[(endOffset + 16)..]);
    var centralSize = BinaryPrimitives.ReadUInt32LittleEndian(
        archive[(endOffset + 12)..]);
    if (centralOffset > int.MaxValue ||
        centralSize > int.MaxValue ||
        (ulong)centralOffset + centralSize != (ulong)endOffset ||
        centralOffset + centralHeaderBytes > endOffset)
    {
      return false;
    }

    var central = archive[(int)centralOffset..];
    var localFlags = BinaryPrimitives.ReadUInt16LittleEndian(archive[6..]);
    var centralFlags = BinaryPrimitives.ReadUInt16LittleEndian(central[8..]);
    var localMethod = BinaryPrimitives.ReadUInt16LittleEndian(archive[8..]);
    var centralMethod = BinaryPrimitives.ReadUInt16LittleEndian(central[10..]);
    return BinaryPrimitives.ReadUInt32LittleEndian(central) ==
        centralHeaderSignature &&
        localFlags == centralFlags &&
        (localFlags & 1) == 0 &&
        localMethod == centralMethod &&
        localMethod is 0 or 8;
  }

  private static bool HasExactProperties(
      JsonElement element,
      FrozenSet<string> expected)
  {
    if (element.ValueKind != JsonValueKind.Object)
    {
      return false;
    }

    var seen = new List<string>(expected.Count);
    using var properties = element.EnumerateObject();
    while (properties.MoveNext())
    {
      var property = properties.Current;
      if (!expected.Contains(property.Name) ||
          seen.Contains(property.Name, StringComparer.Ordinal))
      {
        return false;
      }
      seen.Add(property.Name);
    }
    return seen.Count == expected.Count;
  }

  private static bool GetString(
      JsonElement element,
      string propertyName,
      out string value)
  {
    value = string.Empty;
    if (!element.TryGetProperty(propertyName, out var property) ||
        property.ValueKind != JsonValueKind.String)
    {
      return false;
    }
    value = property.GetString()!;
    return true;
  }

  private static bool GetDateTimeOffset(
      JsonElement element,
      string propertyName,
      out DateTimeOffset value)
  {
    value = default;
    return element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String &&
        property.TryGetDateTimeOffset(out value);
  }

  private static NullableString ReadNullableString(
      JsonElement element,
      string propertyName)
  {
    if (!element.TryGetProperty(propertyName, out var property))
    {
      return new(true, null);
    }
    return property.ValueKind switch
    {
      JsonValueKind.Null => new(false, null),
      JsonValueKind.String => new(false, property.GetString()),
      _ => new(true, null),
    };
  }

  private static bool MapQualificationName(
      string value,
      out ImageCandidateQualificationName name)
  {
    name = value switch
    {
      "image-build" => ImageCandidateQualificationName.ImageBuild,
      "buildkit-digest" => ImageCandidateQualificationName.BuildKitDigest,
      "registry-digest" => ImageCandidateQualificationName.RegistryDigest,
      "oci-manifest" => ImageCandidateQualificationName.OciManifest,
      "builder-cleanup" => ImageCandidateQualificationName.BuilderCleanup,
      _ => (ImageCandidateQualificationName)(-1),
    };
    return Enum.IsDefined(name);
  }

  private static bool MapQualificationStatus(
      string value,
      out ImageCandidateQualificationStatus status)
  {
    status = value switch
    {
      "passed" => ImageCandidateQualificationStatus.Passed,
      "failed" => ImageCandidateQualificationStatus.Failed,
      "unavailable" => ImageCandidateQualificationStatus.Unavailable,
      _ => (ImageCandidateQualificationStatus)(-1),
    };
    return Enum.IsDefined(status);
  }

  private static string ExpectedImmutableReference(
      string imageReference,
      string digest)
  {
    var tagSeparator = imageReference.LastIndexOf(':');
    return $"{imageReference[..tagSeparator]}@{digest}";
  }

  private static bool Matches(Regex regex, string value)
  {
    try
    {
      return regex.IsMatch(value);
    }
    catch (RegexMatchTimeoutException)
    {
      return false;
    }
  }

  private static ImageCandidateArchiveParseOutcome InvalidReport() =>
      Invalid(
          "candidate-report-invalid",
          "The candidate report does not satisfy the trusted schema and identity contract.");

  private static ImageCandidateArchiveParseOutcome Invalid(
      string code,
      string detail) =>
      ImageCandidateArchiveParseOutcome.Invalid(code, detail);

  [GeneratedRegex(
      "^[a-z][a-z0-9-]{0,63}$",
      RegexOptions.CultureInvariant,
      matchTimeoutMilliseconds: 100)]
  private static partial Regex RecipeIdRegex();

  [GeneratedRegex(
      "^[A-Za-z0-9][A-Za-z0-9._:-]*/[A-Za-z0-9][A-Za-z0-9._/-]*:[A-Za-z0-9_][A-Za-z0-9._-]*$",
      RegexOptions.CultureInvariant,
      matchTimeoutMilliseconds: 100)]
  private static partial Regex ImageReferenceRegex();

  [GeneratedRegex(
      "^sha256:[0-9a-f]{64}$",
      RegexOptions.CultureInvariant,
      matchTimeoutMilliseconds: 100)]
  private static partial Regex DigestRegex();

  [GeneratedRegex(
      "^[A-Za-z0-9][A-Za-z0-9._:-]*/[A-Za-z0-9][A-Za-z0-9._/-]*@sha256:[0-9a-f]{64}$",
      RegexOptions.CultureInvariant,
      matchTimeoutMilliseconds: 100)]
  private static partial Regex ImmutableReferenceRegex();

  private readonly record struct NullableString(
      bool Invalid,
      string? Value);
}
