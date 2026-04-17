using Robust.Shared.GameStates;

namespace Content.Shared._HL.Traits.Physical;

/// <summary>
/// Big trait weapon handling rules:
/// - Cannot wield ranged weapons.
/// - Can ignore weapon "requires wield" checks.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class BigWeaponHandlingComponent : Component;
