namespace PitCrew.Connector.Features.Sync;

/// <summary>
/// Releases one locally held profile operation slot.
/// </summary>
internal sealed class LocalProfileOperationLease(
    LocalProfileOperationGate _gate,
    string _profileId) : IDisposable
{
  private bool _released;

  public void Dispose()
  {
    if (_released)
    {
      return;
    }
    _released = true;
    _gate.Release(_profileId);
  }
}
