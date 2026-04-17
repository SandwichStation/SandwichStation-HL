using Robust.Shared.GameStates;

namespace Content.Shared._HL.Traits.Physical;

/// <summary>
/// Marks a hand placeholder entity as a Tiny reserved-hand blocker for a specific held item.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class TinyReservedHandBlockerComponent : Component
{
    [DataField]
    public EntityUid BlockingEntity;
}
