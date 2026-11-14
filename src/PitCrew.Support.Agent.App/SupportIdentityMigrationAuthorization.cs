namespace PitCrew.Support.Agent.App;

internal sealed record SupportIdentityMigrationAuthorization(
    Guid RotationId,
    string OfferDigest,
    string TargetSigningPublicKeySpki,
    string TargetEncryptionPublicKeySpki,
    string TransportCredential);
