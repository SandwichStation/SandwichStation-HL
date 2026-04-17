using Content.Shared.Wieldable;
using Content.Shared._HL.Traits.Physical.Systems; // HardLight
using Robust.Shared.GameStates;

namespace Content.Shared.Weapons.Melee.Components;

/// <summary>
/// Indicates that this meleeweapon requires wielding to be useable.
/// </summary>
[RegisterComponent, NetworkedComponent, Access(typeof(SharedWieldableSystem), typeof(SharedTraitWeaponHandlingSystem))] // HardLight: Added typeof(SharedTraitWeaponHandlingSystem)
[AutoGenerateComponentState] // HardLight
public sealed partial class MeleeRequiresWieldComponent : Component
{
    // HardLight start
	[DataField, AutoNetworkedField]
	public TimeSpan LastPopup;

	[DataField, AutoNetworkedField]
	public TimeSpan PopupCooldown = TimeSpan.FromSeconds(1);
    // HardLight end
}
