using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

using PitCrew.Dashboard.Features.Images.Abstractions;

namespace PitCrew.Dashboard.Features.Images.Tests;

internal static class ImageCandidateArchiveTestData
{
  public static string CreateReadyReport(
      string sourceRepository = "ncosentino/pitcrew",
      string sourceCommit =
          "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
      long workflowRunId = 7001,
      int schemaVersion = 1) =>
      $$"""
      {
        "schemaVersion": {{schemaVersion}},
        "status": "ready",
        "recipeId": "pitcrew-default",
        "createdAt": "2026-08-24T02:05:00Z",
        "source": {
          "repository": "{{sourceRepository}}",
          "commit": "{{sourceCommit}}",
          "workflowRunId": {{workflowRunId}}
        },
        "image": {
          "reference": "ghcr.io/ncosentino/pitcrew:test",
          "digest": "sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd",
          "immutableReference": "ghcr.io/ncosentino/pitcrew@sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd",
          "platform": "linux/amd64",
          "outputMode": "registry"
        },
        "qualifications": [
          { "name": "image-build", "status": "passed" },
          { "name": "buildkit-digest", "status": "passed" },
          { "name": "registry-digest", "status": "passed" },
          { "name": "builder-cleanup", "status": "passed" }
        ],
        "failureCategory": null,
        "failureDetail": null
      }
      """;

  public static string CreateFailedReport() =>
      """
      {
        "schemaVersion": 1,
        "status": "failed",
        "recipeId": "pitcrew-default",
        "createdAt": "2026-08-24T02:05:00Z",
        "source": {
          "repository": "ncosentino/pitcrew",
          "commit": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
          "workflowRunId": 7001
        },
        "image": {
          "reference": "ghcr.io/ncosentino/pitcrew:test",
          "digest": null,
          "immutableReference": null,
          "platform": "linux/amd64",
          "outputMode": "registry"
        },
        "qualifications": [
          { "name": "image-build", "status": "failed" },
          { "name": "buildkit-digest", "status": "unavailable" },
          { "name": "registry-digest", "status": "unavailable" },
          { "name": "builder-cleanup", "status": "passed" }
        ],
        "failureCategory": "build-failed",
        "failureDetail": "Image build did not complete."
      }
      """;

  public static byte[] CreateArchive(string report) =>
      CreateArchive(
          [(ImageCandidateArchiveParser.ReportName,
              Encoding.UTF8.GetBytes(report),
              RegularFileAttributes)]);

  public static byte[] CreateArchive(
      params (string Name, byte[] Content, int Attributes)[] entries)
  {
    using var stream = new MemoryStream();
    using (var archive = new ZipArchive(
        stream,
        ZipArchiveMode.Create,
        leaveOpen: true,
        entryNameEncoding: new UTF8Encoding(false, true)))
    {
      foreach (var value in entries)
      {
        var entry = archive.CreateEntry(
            value.Name,
            CompressionLevel.SmallestSize);
        entry.ExternalAttributes = value.Attributes;
        using var entryStream = entry.Open();
        entryStream.Write(value.Content);
      }
    }
    return stream.ToArray();
  }

  public static byte[] WithUnsupportedCompression(byte[] archive)
  {
    var copy = archive.ToArray();
    for (var index = 0; index <= copy.Length - 4; index++)
    {
      var signature = BinaryPrimitives.ReadUInt32LittleEndian(
          copy.AsSpan(index));
      if (signature == 0x04034B50)
      {
        BinaryPrimitives.WriteUInt16LittleEndian(
            copy.AsSpan(index + 8),
            99);
      }
      else if (signature == 0x02014B50)
      {
        BinaryPrimitives.WriteUInt16LittleEndian(
            copy.AsSpan(index + 10),
            99);
      }
    }
    return copy;
  }

  public static GitHubWorkflowArtifact CreateArtifact(
      byte[] archive,
      DateTimeOffset now,
      string name = ImageCandidateArchiveParser.ArtifactName) =>
      new(
          8001,
          7001,
          name,
          archive.Length,
          $"sha256:{Convert.ToHexStringLower(SHA256.HashData(archive))}",
          false,
          now.AddHours(1),
          new Uri(
              "https://api.github.com/repos/ncosentino/pitcrew/actions/artifacts/8001/zip",
              UriKind.Absolute));

  public const int RegularFileAttributes = unchecked((int)0x81A40000);
  public const int SymbolicLinkAttributes = unchecked((int)0xA1FF0000);
  public const int DirectoryAttributes = unchecked((int)0x41FF0010);
}
