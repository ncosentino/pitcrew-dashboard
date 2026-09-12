using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace PitCrew.Protocol;

/// <summary>
/// Describes one profile's unit accounting within the host-local admission budget.
/// </summary>
public sealed record HostAdmissionAccounting
{
  private const int AllocatableUnitsBit = 1;
  private const int AllocatableWorkersBit = 2;
  private const int TheoreticalMaximumUnitsBit = 4;
  private const int TheoreticalMaximumWorkersBit = 8;
  private const int WithholdingReasonBit = 16;
  private const int ContractNineteenMask =
      AllocatableUnitsBit |
      AllocatableWorkersBit |
      TheoreticalMaximumUnitsBit |
      TheoreticalMaximumWorkersBit |
      WithholdingReasonBit;
  private static readonly ConditionalWeakTable<
      HostAdmissionAccounting,
      ContractNineteenPresence> ContractNineteenPresences = new();

  private int? _allocatableUnits;
  private int? _allocatableWorkers;
  private int? _theoreticalMaximumUnits;
  private int? _theoreticalMaximumWorkers;
  private string? _withholdingReason;

  /// <summary>
  /// Initializes an empty instance for JSON deserialization.
  /// </summary>
  [JsonConstructor]
  public HostAdmissionAccounting()
  {
  }

  /// <summary>
  /// Initializes manager contract 18 accounting.
  /// </summary>
  public HostAdmissionAccounting(
      int UnitCost,
      int ReservedUnits,
      bool Borrowable,
      string? ProfilePolicyFingerprint,
      int ActiveUnits,
      int ProvisionalUnits,
      int HeldUnits,
      int BorrowedUnits,
      int? PendingUnits,
      int? WithheldUnits)
  {
    this.UnitCost = UnitCost;
    this.ReservedUnits = ReservedUnits;
    this.Borrowable = Borrowable;
    this.ProfilePolicyFingerprint = ProfilePolicyFingerprint;
    this.ActiveUnits = ActiveUnits;
    this.ProvisionalUnits = ProvisionalUnits;
    this.HeldUnits = HeldUnits;
    this.BorrowedUnits = BorrowedUnits;
    this.PendingUnits = PendingUnits;
    this.WithheldUnits = WithheldUnits;
  }

  /// <summary>
  /// Initializes manager contract 19 accounting with profile-usable capacity evidence.
  /// </summary>
  public HostAdmissionAccounting(
      int UnitCost,
      int ReservedUnits,
      bool Borrowable,
      string? ProfilePolicyFingerprint,
      int ActiveUnits,
      int ProvisionalUnits,
      int HeldUnits,
      int BorrowedUnits,
      int? PendingUnits,
      int? WithheldUnits,
      int? AllocatableUnits,
      int? AllocatableWorkers,
      int? TheoreticalMaximumUnits,
      int? TheoreticalMaximumWorkers,
      string? WithholdingReason)
      : this(
          UnitCost,
          ReservedUnits,
          Borrowable,
          ProfilePolicyFingerprint,
          ActiveUnits,
          ProvisionalUnits,
          HeldUnits,
          BorrowedUnits,
          PendingUnits,
          WithheldUnits)
  {
    this.AllocatableUnits = AllocatableUnits;
    this.AllocatableWorkers = AllocatableWorkers;
    this.TheoreticalMaximumUnits = TheoreticalMaximumUnits;
    this.TheoreticalMaximumWorkers = TheoreticalMaximumWorkers;
    this.WithholdingReason = WithholdingReason;
  }

  private HostAdmissionAccounting(HostAdmissionAccounting original)
  {
    UnitCost = original.UnitCost;
    ReservedUnits = original.ReservedUnits;
    Borrowable = original.Borrowable;
    ProfilePolicyFingerprint = original.ProfilePolicyFingerprint;
    ActiveUnits = original.ActiveUnits;
    ProvisionalUnits = original.ProvisionalUnits;
    HeldUnits = original.HeldUnits;
    BorrowedUnits = original.BorrowedUnits;
    PendingUnits = original.PendingUnits;
    WithheldUnits = original.WithheldUnits;
    _allocatableUnits = original._allocatableUnits;
    _allocatableWorkers = original._allocatableWorkers;
    _theoreticalMaximumUnits = original._theoreticalMaximumUnits;
    _theoreticalMaximumWorkers = original._theoreticalMaximumWorkers;
    _withholdingReason = original._withholdingReason;
    if (ContractNineteenPresences.TryGetValue(original, out var presence))
    {
      ContractNineteenPresences.Add(
          this,
          new ContractNineteenPresence { Mask = presence.Mask });
    }
  }

  /// <summary>
  /// Gets the abstract units consumed by one admitted worker.
  /// </summary>
  [JsonRequired]
  public int UnitCost { get; init; }

  /// <summary>
  /// Gets the units reserved for this profile.
  /// </summary>
  [JsonRequired]
  public int ReservedUnits { get; init; }

  /// <summary>
  /// Gets whether other profiles may borrow unused reserved units.
  /// </summary>
  [JsonRequired]
  public bool Borrowable { get; init; }

  /// <summary>
  /// Gets the opaque identity of the applied profile policy.
  /// </summary>
  [JsonRequired]
  public string? ProfilePolicyFingerprint { get; init; }

  /// <summary>
  /// Gets the units held by active leases.
  /// </summary>
  [JsonRequired]
  public int ActiveUnits { get; init; }

  /// <summary>
  /// Gets the units held by provisional leases.
  /// </summary>
  [JsonRequired]
  public int ProvisionalUnits { get; init; }

  /// <summary>
  /// Gets the combined active and provisional units.
  /// </summary>
  [JsonRequired]
  public int HeldUnits { get; init; }

  /// <summary>
  /// Gets the held units beyond this profile's reservation.
  /// </summary>
  [JsonRequired]
  public int BorrowedUnits { get; init; }

  /// <summary>
  /// Gets outstanding demand in policy units, or <see langword="null"/> when demand freshness is unknown.
  /// </summary>
  [JsonRequired]
  public int? PendingUnits { get; init; }

  /// <summary>
  /// Gets outstanding ungranted units, or <see langword="null"/> when demand freshness is unknown.
  /// </summary>
  [JsonRequired]
  public int? WithheldUnits { get; init; }

  /// <summary>
  /// Gets the units currently usable by this profile.
  /// </summary>
  public int? AllocatableUnits
  {
    get => _allocatableUnits;
    init
    {
      _allocatableUnits = value;
      MarkContractNineteenProperty(AllocatableUnitsBit);
    }
  }

  /// <summary>
  /// Gets the whole workers currently usable by this profile.
  /// </summary>
  public int? AllocatableWorkers
  {
    get => _allocatableWorkers;
    init
    {
      _allocatableWorkers = value;
      MarkContractNineteenProperty(AllocatableWorkersBit);
    }
  }

  /// <summary>
  /// Gets the profile's static theoretical maximum units before shared contention.
  /// </summary>
  public int? TheoreticalMaximumUnits
  {
    get => _theoreticalMaximumUnits;
    init
    {
      _theoreticalMaximumUnits = value;
      MarkContractNineteenProperty(TheoreticalMaximumUnitsBit);
    }
  }

  /// <summary>
  /// Gets the profile's static theoretical maximum whole workers before shared contention.
  /// </summary>
  public int? TheoreticalMaximumWorkers
  {
    get => _theoreticalMaximumWorkers;
    init
    {
      _theoreticalMaximumWorkers = value;
      MarkContractNineteenProperty(TheoreticalMaximumWorkersBit);
    }
  }

  /// <summary>
  /// Gets the bounded coordinator-owned reason that admission is currently withheld.
  /// </summary>
  public string? WithholdingReason
  {
    get => _withholdingReason;
    init
    {
      _withholdingReason = value;
      MarkContractNineteenProperty(WithholdingReasonBit);
    }
  }

  /// <summary>
  /// Gets whether every additive manager contract 19 property was explicitly supplied.
  /// </summary>
  [JsonIgnore]
  public bool HasCompleteContractNineteenEvidence =>
      ContractNineteenPresences.TryGetValue(this, out var presence) &&
      presence.Mask == ContractNineteenMask;

  /// <summary>
  /// Deconstructs the manager contract 18 accounting fields.
  /// </summary>
  /// <param name="UnitCost">The abstract units consumed by one admitted worker.</param>
  /// <param name="ReservedUnits">The units reserved for this profile.</param>
  /// <param name="Borrowable">Whether other profiles may borrow unused reserved units.</param>
  /// <param name="ProfilePolicyFingerprint">The opaque identity of the applied profile policy.</param>
  /// <param name="ActiveUnits">The units held by active leases.</param>
  /// <param name="ProvisionalUnits">The units held by provisional leases.</param>
  /// <param name="HeldUnits">The combined active and provisional units.</param>
  /// <param name="BorrowedUnits">The held units beyond this profile's reservation.</param>
  /// <param name="PendingUnits">Outstanding demand in policy units.</param>
  /// <param name="WithheldUnits">Outstanding ungranted units.</param>
  public void Deconstruct(
      out int UnitCost,
      out int ReservedUnits,
      out bool Borrowable,
      out string? ProfilePolicyFingerprint,
      out int ActiveUnits,
      out int ProvisionalUnits,
      out int HeldUnits,
      out int BorrowedUnits,
      out int? PendingUnits,
      out int? WithheldUnits)
  {
    UnitCost = this.UnitCost;
    ReservedUnits = this.ReservedUnits;
    Borrowable = this.Borrowable;
    ProfilePolicyFingerprint = this.ProfilePolicyFingerprint;
    ActiveUnits = this.ActiveUnits;
    ProvisionalUnits = this.ProvisionalUnits;
    HeldUnits = this.HeldUnits;
    BorrowedUnits = this.BorrowedUnits;
    PendingUnits = this.PendingUnits;
    WithheldUnits = this.WithheldUnits;
  }

  private void MarkContractNineteenProperty(int propertyBit) =>
      ContractNineteenPresences.GetOrCreateValue(this).Mask |= propertyBit;

  private sealed class ContractNineteenPresence
  {
    public int Mask { get; set; }
  }
}
