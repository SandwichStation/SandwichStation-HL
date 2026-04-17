using Content.Server.Body.Systems;

namespace Content.Server.Body.Components;

/// <summary>
/// Tracks when an entity's blood is being modified by reagents or other effects.
/// Used to prevent blood restoration while active blood-changing effects are present.
/// This component is automatically added/removed by blood-changing effects.
/// </summary>
[RegisterComponent, Access(typeof(BloodstreamSystem))]
public sealed partial class BloodModificationTrackerComponent : Component
{
    /// <summary>
    /// Counter of active blood-modifying effects.
    /// When this reaches zero, the component should be removed.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public int ActiveEffects = 0;
}