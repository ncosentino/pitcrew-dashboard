namespace PitCrew.Support.Agent.App;

internal sealed record SupportNodeKeyDescriptor(
    string Provider,
    string KeySetId,
    string SigningKeyReference,
    string EncryptionKeyReference,
    string SigningPublicKeySpki,
    string EncryptionPublicKeySpki);
