using System.Globalization;
using System.Text;

using PitCrew.Dashboard.Features.Images.Abstractions;

namespace PitCrew.Dashboard.Features.Images.Tests;

public sealed class ImageCandidateArchiveParserTests
{
  [Test]
  public async Task Valid_Ready_Report_Produces_Immutable_Candidate()
  {
    var claim = CreateClaim();
    var report = ImageCandidateArchiveTestData.CreateReadyReport();
    var archive = ImageCandidateArchiveTestData.CreateArchive(report);
    var artifact = ImageCandidateArchiveTestData.CreateArtifact(
        archive,
        claim.Request.UpdatedAt);

    var result = ImageCandidateArchiveParser.Parse(
        claim,
        artifact,
        new GitHubWorkflowArtifactArchive(artifact.Id, archive),
        262_144,
        32_768);

    await Assert.That(result.Succeeded).IsTrue()
        .Because("the report matches the frozen request and schema");
    await Assert.That(result.Candidate)
        .IsTypeOf<ReadyImageCandidate>();
    var candidate = (ReadyImageCandidate)result.Candidate!;
    await Assert.That(candidate.CandidateId)
        .IsEqualTo(claim.Request.RequestId);
    await Assert.That(candidate.ReportJson).IsEqualTo(report);
    await Assert.That(candidate.ArtifactDigest)
        .IsEqualTo(artifact.Digest);
    await Assert.That(candidate.ImmutableReference)
        .IsEqualTo(
            "ghcr.io/ncosentino/pitcrew@sha256:dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd");
    await Assert.That(result.Qualifications).Count().IsEqualTo(4);
    await Assert.That(result.Qualifications.All(qualification =>
            qualification.Status ==
                ImageCandidateQualificationStatus.Passed))
        .IsTrue()
        .Because("ready candidates require every qualification to pass");
  }

  [Test]
  [Arguments("extra")]
  [Arguments("traversal")]
  [Arguments("absolute")]
  [Arguments("link")]
  [Arguments("directory")]
  [Arguments("duplicate")]
  [Arguments("compression")]
  public async Task Unsafe_Archive_Shapes_Are_Rejected(string scenario)
  {
    var claim = CreateClaim();
    var report = Encoding.UTF8.GetBytes(
        ImageCandidateArchiveTestData.CreateReadyReport());
    var archive = scenario switch
    {
      "extra" => ImageCandidateArchiveTestData.CreateArchive(
          (ImageCandidateArchiveParser.ReportName,
              report,
              ImageCandidateArchiveTestData.RegularFileAttributes),
          ("extra.txt",
              "extra"u8.ToArray(),
              ImageCandidateArchiveTestData.RegularFileAttributes)),
      "traversal" => ImageCandidateArchiveTestData.CreateArchive(
          ($"../{ImageCandidateArchiveParser.ReportName}",
              report,
              ImageCandidateArchiveTestData.RegularFileAttributes)),
      "absolute" => ImageCandidateArchiveTestData.CreateArchive(
          ($"/{ImageCandidateArchiveParser.ReportName}",
              report,
              ImageCandidateArchiveTestData.RegularFileAttributes)),
      "link" => ImageCandidateArchiveTestData.CreateArchive(
          (ImageCandidateArchiveParser.ReportName,
              report,
              ImageCandidateArchiveTestData.SymbolicLinkAttributes)),
      "directory" => ImageCandidateArchiveTestData.CreateArchive(
          ($"{ImageCandidateArchiveParser.ReportName}/",
              report,
              ImageCandidateArchiveTestData.DirectoryAttributes)),
      "duplicate" => ImageCandidateArchiveTestData.CreateArchive(
          (ImageCandidateArchiveParser.ReportName,
              report,
              ImageCandidateArchiveTestData.RegularFileAttributes),
          (ImageCandidateArchiveParser.ReportName,
              report,
              ImageCandidateArchiveTestData.RegularFileAttributes)),
      "compression" => ImageCandidateArchiveTestData.WithUnsupportedCompression(
          ImageCandidateArchiveTestData.CreateArchive(
              ImageCandidateArchiveTestData.CreateReadyReport())),
      _ => [],
    };
    var artifact = ImageCandidateArchiveTestData.CreateArtifact(
        archive,
        claim.Request.UpdatedAt);

    var result = ImageCandidateArchiveParser.Parse(
        claim,
        artifact,
        new GitHubWorkflowArtifactArchive(artifact.Id, archive),
        262_144,
        32_768);

    await Assert.That(result.Succeeded).IsFalse()
        .Because("unsafe archive structure cannot create a candidate");
    await Assert.That(result.Candidate).IsNull();
  }

  [Test]
  [Arguments("utf8", "candidate-report-utf8-invalid")]
  [Arguments("json", "candidate-report-json-invalid")]
  [Arguments("schema", "candidate-report-schema-unsupported")]
  [Arguments("identity", "candidate-report-invalid")]
  [Arguments("qualifications", "candidate-report-invalid")]
  public async Task Invalid_Report_Evidence_Is_Rejected(
      string scenario,
      string expectedCode)
  {
    var claim = CreateClaim();
    var valid = ImageCandidateArchiveTestData.CreateReadyReport();
    var content = scenario switch
    {
      "utf8" => new byte[] { 0xC3, 0x28 },
      "json" => "{"u8.ToArray(),
      "schema" => Encoding.UTF8.GetBytes(
          ImageCandidateArchiveTestData.CreateReadyReport(
              schemaVersion: 2)),
      "identity" => Encoding.UTF8.GetBytes(
          ImageCandidateArchiveTestData.CreateReadyReport(
              sourceRepository: "other/repository")),
      "qualifications" => Encoding.UTF8.GetBytes(
          valid.Replace(
              "\"builder-cleanup\"",
              "\"registry-digest\"",
              StringComparison.Ordinal)),
      _ => [],
    };
    var archive = ImageCandidateArchiveTestData.CreateArchive(
        (ImageCandidateArchiveParser.ReportName,
            content,
            ImageCandidateArchiveTestData.RegularFileAttributes));
    var artifact = ImageCandidateArchiveTestData.CreateArtifact(
        archive,
        claim.Request.UpdatedAt);

    var result = ImageCandidateArchiveParser.Parse(
        claim,
        artifact,
        new GitHubWorkflowArtifactArchive(artifact.Id, archive),
        262_144,
        32_768);

    await Assert.That(result.ErrorCode).IsEqualTo(expectedCode);
    await Assert.That(result.Candidate).IsNull();
  }

  [Test]
  public async Task Expanded_Report_Beyond_Bound_Is_Rejected()
  {
    var claim = CreateClaim();
    var content = Encoding.UTF8.GetBytes(new string('x', 32_769));
    var archive = ImageCandidateArchiveTestData.CreateArchive(
        (ImageCandidateArchiveParser.ReportName,
            content,
            ImageCandidateArchiveTestData.RegularFileAttributes));
    var artifact = ImageCandidateArchiveTestData.CreateArtifact(
        archive,
        claim.Request.UpdatedAt);

    var result = ImageCandidateArchiveParser.Parse(
        claim,
        artifact,
        new GitHubWorkflowArtifactArchive(artifact.Id, archive),
        262_144,
        32_768);

    await Assert.That(result.ErrorCode)
        .IsEqualTo("candidate-archive-invalid");
    await Assert.That(result.Candidate).IsNull();
  }

  private static ImageBuildExecutionClaim CreateClaim()
  {
    var now = new DateTimeOffset(
        2026,
        8,
        24,
        2,
        0,
        0,
        TimeSpan.Zero);
    var registration = new ImageRecipeRegistration(
        "tenant-a",
        Guid.Parse(
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            CultureInfo.InvariantCulture),
        1,
        1001,
        2001,
        3001,
        "ncosentino",
        "pitcrew",
        ".github/workflows/image-candidate.yml",
        new string('a', 40),
        "release/v1",
        "pitcrew-default",
        1,
        """{"allowedSourceRefs":["refs/heads/main"]}""",
        """{"type":"object"}""",
        "owner-user",
        now,
        null,
        null);
    var request = new ImageBuildRequest(
        "tenant-a",
        Guid.Parse(
            "88888888-8888-8888-8888-888888888888",
            CultureInfo.InvariantCulture),
        registration.RegistrationId,
        registration.Version,
        registration.RecipeId,
        "ncosentino/pitcrew",
        new string('b', 40),
        "{}",
        new string('c', 64),
        "owner-user",
        now,
        ImageBuildRequestStatus.Qualifying,
        7001,
        "https://github.com/ncosentino/pitcrew/actions/runs/7001",
        null,
        null,
        now.AddMinutes(4),
        "refs/heads/main",
        "https://api.github.com/repos/ncosentino/pitcrew/actions/runs/7001");
    return new ImageBuildExecutionClaim(
        request,
        registration,
        "worker",
        now.AddMinutes(5),
        false,
        1,
        1,
        0,
        0,
        now);
  }
}
