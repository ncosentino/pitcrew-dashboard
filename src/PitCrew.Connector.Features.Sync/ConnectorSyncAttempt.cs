using System.Net;

using PitCrew.Protocol;

namespace PitCrew.Connector.Features.Sync;

internal sealed record ConnectorSyncAttempt(
    HttpStatusCode StatusCode,
    ConnectorSyncResponse? Response,
    ConnectorSyncRejectionReason RejectionReason = ConnectorSyncRejectionReason.Unspecified);
