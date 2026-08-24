using PitCrew.Support.Protocol;

namespace PitCrew.Support.Protocol.Tests;

public sealed class SupportRequestRejectionDispositionTests
{
  [Test]
  public async Task Rejection_Dispositions_Are_Closed_Bounded_And_Unique()
  {
    var dispositions =
        SupportRequestRejectionDispositions.All;

    await Assert.That(dispositions.Count).IsEqualTo(24);
    await Assert.That(
            dispositions.Distinct(
                StringComparer.Ordinal).Count())
        .IsEqualTo(dispositions.Count);
    await Assert.That(
            dispositions.All(disposition =>
                disposition.Length is >= 1 and <= 64 &&
                disposition.All(character =>
                    character is >= 'a' and <= 'z' or
                    '-')))
        .IsTrue()
        .Because(
            "relay lifecycle metadata must remain a bounded closed vocabulary");
    await Assert.That(
            SupportRequestRejectionDispositions.IsSupported(
                "request-body-content"))
        .IsFalse()
        .Because("free-form rejection reasons are prohibited");
  }
}
