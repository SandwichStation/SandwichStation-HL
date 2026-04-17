namespace Content.Shared._HL.Traits.Physical;

[RegisterComponent]
public sealed partial class HeldByBigWeaponHandlingComponent : Component
{
    public EntityUid Holder = default!;

    public double MinAngleAdded;
    public double MaxAngleAdded;
    public double AngleIncreaseAdded;
}
