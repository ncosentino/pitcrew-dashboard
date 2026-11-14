using PitCrew.Support.Protocol;

namespace PitCrew.Support.Agent.App;

internal sealed record SupportIdentityMigrationRequest(
    int SchemaVersion,
    Guid RotationId,
    string OfferDigest,
    string TargetSigningPublicKeySpki,
    string TargetEncryptionPublicKeySpki,
    SupportEnvelope CredentialEnvelope);
