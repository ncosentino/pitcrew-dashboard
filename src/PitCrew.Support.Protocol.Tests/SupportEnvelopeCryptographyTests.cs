using System.Globalization;
using System.Text;
using System.Text.Json;

using PitCrew.Support.Protocol;

namespace PitCrew.Support.Protocol.Tests;

public sealed class SupportEnvelopeCryptographyTests
{
  [Test]
  public async Task Sealed_Request_Round_Trips_And_Tampering_Fails()
  {
    var dashboardKeys = SupportKeyFactory.CreateDashboardKeys();
    var nodeKeys = SupportKeyFactory.CreateNodeKeys();
    using var dashboardSigning = SupportKeyFactory.ImportEcdsaPrivateKey(
        dashboardKeys.AuthorizationSigning.PrivateKeyPkcs8Base64Url);
    using var nodeSigningPublic = SupportKeyFactory.ImportEcdsaPublicKey(
        dashboardKeys.AuthorizationSigning.PublicKeySubjectPublicKeyInfoBase64Url);
    using var nodeEncryptionPublic = SupportKeyFactory.ImportRsaPublicKey(
        nodeKeys.Encryption.PublicKeySubjectPublicKeyInfoBase64Url);
    using var nodeEncryptionPrivate = SupportKeyFactory.ImportRsaPrivateKey(
        nodeKeys.Encryption.PrivateKeyPkcs8Base64Url);
    var request = new SupportDiagnosticRequest(
        "support-plane-v1",
        "tenant-a",
        Guid.Parse("11111111-1111-1111-1111-111111111111", CultureInfo.InvariantCulture),
        Guid.Parse("22222222-2222-2222-2222-222222222222", CultureInfo.InvariantCulture),
        "pitcrew.remote-diagnostics",
        1,
        SupportDiagnosticModes.HostPressure,
        "default",
        "pkg-1",
        DateTimeOffset.Parse("2026-08-01T00:00:00+00:00", CultureInfo.InvariantCulture),
        DateTimeOffset.Parse("2026-08-01T00:05:00+00:00", CultureInfo.InvariantCulture),
        "nonce-abcdefghijklmnopqrstuvwxyz0123456789");

    var envelope = SupportEnvelopeCryptography.Seal(
        SupportCanonicalJson.SerializeRequest(request),
        nodeEncryptionPublic,
        dashboardSigning,
        "dashboard-auth",
        "node-enc");
    var opened = SupportEnvelopeCryptography.OpenOrNull(
        envelope,
        nodeSigningPublic,
        nodeEncryptionPrivate);
    var tampered = envelope with
    {
      CiphertextBase64Url = envelope.CiphertextBase64Url[..^1] +
          (envelope.CiphertextBase64Url[^1] == 'A' ? 'B' : 'A'),
    };

    await Assert.That(opened).IsNotNull();
    await Assert.That(Encoding.UTF8.GetString(opened!))
        .IsEqualTo(Encoding.UTF8.GetString(SupportCanonicalJson.SerializeRequest(request)));
    await Assert.That(SupportEnvelopeCryptography.OpenOrNull(
            tampered,
            nodeSigningPublic,
            nodeEncryptionPrivate))
        .IsNull();
  }

  [Test]
  public async Task Request_Validation_Rejects_Replay_Expiry_And_Wrong_Node()
  {
    var now = DateTimeOffset.Parse("2026-08-01T00:01:00+00:00", CultureInfo.InvariantCulture);
    var nodeId = Guid.Parse("11111111-1111-1111-1111-111111111111", CultureInfo.InvariantCulture);
    var request = new SupportDiagnosticRequest(
        "support-plane-v1",
        "tenant-a",
        nodeId,
        Guid.Parse("22222222-2222-2222-2222-222222222222", CultureInfo.InvariantCulture),
        "pitcrew.remote-diagnostics",
        1,
        SupportDiagnosticModes.Full,
        null,
        "pkg-1",
        now.AddMinutes(-1),
        now.AddMinutes(4),
        "nonce-abcdefghijklmnopqrstuvwxyz0123456789");

    var valid = SupportRequestValidator.Validate(
        request,
        "tenant-a",
        nodeId,
        now,
        _ => false);
    var replay = SupportRequestValidator.Validate(
        request,
        "tenant-a",
        nodeId,
        now,
        _ => true);
    var expired = SupportRequestValidator.Validate(
        request with { ExpiresAt = now.AddTicks(-1) },
        "tenant-a",
        nodeId,
        now,
        _ => false);
    var wrongNode = SupportRequestValidator.Validate(
        request,
        "tenant-a",
        Guid.Parse("33333333-3333-3333-3333-333333333333", CultureInfo.InvariantCulture),
        now,
        _ => false);

    await Assert.That(valid).IsEqualTo(SupportRequestValidationStatus.Valid);
    await Assert.That(replay).IsEqualTo(SupportRequestValidationStatus.Replay);
    await Assert.That(expired).IsEqualTo(SupportRequestValidationStatus.Expired);
    await Assert.That(wrongNode).IsEqualTo(SupportRequestValidationStatus.WrongTenantOrNode);
  }

  [Test]
  public async Task Result_Attestation_Uses_Canonical_Payload()
  {
    var nodeKeys = SupportKeyFactory.CreateNodeKeys();
    using var nodeSigning = SupportKeyFactory.ImportEcdsaPrivateKey(
        nodeKeys.Signing.PrivateKeyPkcs8Base64Url);
    using var report = JsonDocument.Parse("{\"verified\":[\"capacity\"],\"unavailable\":[],\"hypotheses\":[]}");
    var payload = new SupportResultPayload(
        "tenant-a",
        Guid.Parse("11111111-1111-1111-1111-111111111111", CultureInfo.InvariantCulture),
        Guid.Parse("22222222-2222-2222-2222-222222222222", CultureInfo.InvariantCulture),
        report.RootElement.Clone(),
        "# Report");
    var canonical = SupportCanonicalJson.SerializeResultAttestationPayload(payload);
    var signature = nodeSigning.SignData(
        canonical,
        System.Security.Cryptography.HashAlgorithmName.SHA256,
        System.Security.Cryptography.DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    var attestation = new SupportResultAttestation(
        Convert.ToBase64String(SupportBase64Url.Decode(nodeKeys.Signing.PublicKeySubjectPublicKeyInfoBase64Url)),
        SupportBase64Url.Encode(canonical),
        SupportBase64Url.Encode(signature),
        SupportEnvelopeCryptography.SignatureAlgorithm);

    await Assert.That(SupportEnvelopeCryptography.VerifyAttestation(attestation))
        .IsTrue()
        .Because("the payload and signature were produced from the same node key");
    await Assert.That(SupportEnvelopeCryptography.VerifyAttestation(
            attestation with { PayloadBase64Url = SupportBase64Url.Encode(Encoding.UTF8.GetBytes("{}")) }))
        .IsFalse()
        .Because("altering the canonical payload must invalidate the signature");
  }
}

