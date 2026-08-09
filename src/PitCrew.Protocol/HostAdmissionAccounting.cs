using System.Text.Json.Serialization;

namespace PitCrew.Protocol;

/// <summary>
/// Describes one profile's unit accounting within the host-local admission budget.
/// </summary>
/// <param name="UnitCost">Abstract units consumed by one admitted worker.</param>
/// <param name="ReservedUnits">Units reserved for this profile.</param>
/// <param name="Borrowable">Whether other profiles may borrow unused reserved units.</param>
/// <param name="ProfilePolicyFingerprint">Opaque identity of the applied profile policy.</param>
/// <param name="ActiveUnits">Units held by active leases.</param>
/// <param name="ProvisionalUnits">Units held by provisional leases.</param>
/// <param name="HeldUnits">Combined active and provisional units.</param>
/// <param name="BorrowedUnits">Held units beyond this profile's reservation.</param>
/// <param name="PendingUnits">Outstanding demand in policy units, or <see langword="null"/> when demand freshness is unknown.</param>
/// <param name="WithheldUnits">Outstanding ungranted units, or <see langword="null"/> when demand freshness is unknown.</param>
public sealed record HostAdmissionAccounting(
    [property: JsonRequired] int UnitCost,
    [property: JsonRequired] int ReservedUnits,
    [property: JsonRequired] bool Borrowable,
    [property: JsonRequired] string? ProfilePolicyFingerprint,
    [property: JsonRequired] int ActiveUnits,
    [property: JsonRequired] int ProvisionalUnits,
    [property: JsonRequired] int HeldUnits,
    [property: JsonRequired] int BorrowedUnits,
    [property: JsonRequired] int? PendingUnits,
    [property: JsonRequired] int? WithheldUnits);
