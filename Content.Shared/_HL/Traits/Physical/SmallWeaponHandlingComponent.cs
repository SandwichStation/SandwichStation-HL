using Robust.Shared.GameStates;
using Content.Shared.Whitelist;

namespace Content.Shared._HL.Traits.Physical;

/// <summary>
/// Small trait weapon handling rules:
/// - Cannot use weapons that require wielding.
/// - Must wield wieldable weapons that would normally not require wielding.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SmallWeaponHandlingComponent : Component
{
	/// <summary>
	/// Items excluded from Small weapon-handling restrictions.
	/// Defaults allow food/drink items to be used normally even if they also have weapon components.
	/// </summary>
	[DataField]
	public EntityWhitelist WeaponHandlingExcluder = new()
	{
		Components = new[] { "Food", "Drink" }
	};
}
