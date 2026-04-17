using Robust.Shared.GameStates;
using Content.Shared.Whitelist;

namespace Content.Shared._HL.Traits.Physical;

/// <summary>
/// Tiny trait weapon handling rules:
/// - Cannot pick up item weapons (gun/melee).
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class TinyWeaponHandlingComponent : Component
{
    [DataField]
    public TimeSpan LastPopup;

    [DataField]
    public TimeSpan PopupCooldown = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Utility component exclusions for Tiny pickup blocking.
    /// Defaults keep Food / Drink / Tool items pickup-able even if they also have weapon components.
    /// </summary>
    [DataField]
    public EntityWhitelist PickupWeaponExcluder = new()
    {
        Components = new[] { "Food", "Drink", "Tool" }
    };
}
