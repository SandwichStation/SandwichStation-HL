using Content.Shared.Wieldable;
using Content.Shared._HL.Traits.Physical.Systems; // HardLight
using Robust.Shared.GameStates;

namespace Content.Shared.Weapons.Ranged.Components;

/// <summary>
/// Indicates that this gun requires wielding to be useable.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedWieldableSystem), typeof(SharedTraitWeaponHandlingSystem))] // HardLight: Added typeof(SharedTraitWeaponHandlingSystem)
public sealed partial class GunRequiresWieldComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan LastPopup;

    [DataField, AutoNetworkedField]
    public TimeSpan PopupCooldown = TimeSpan.FromSeconds(1);

    [DataField]
    public LocId? WieldRequiresExamineMessage  = "gunrequireswield-component-examine";
}
