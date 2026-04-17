using Content.Shared.Whitelist;

namespace Content.Server.Explosion.Components;

/// <summary>
///     Triggers when colliding with another entity.
/// </summary>
[RegisterComponent]
public sealed partial class TriggerOnCollideComponent : Component
{
    /// <summary>
    ///     The fixture with which to collide.
    /// </summary>
    [DataField(required: true)]
    public string FixtureID = string.Empty;

    /// <summary>
    ///     Doesn't trigger if the other colliding fixture is nonhard.
    /// </summary>
    [DataField]
    public bool IgnoreOtherNonHard = true;

    /// <summary>
    ///     If specified, only entities matching this whitelist will trigger the collision.
    ///     Use this to make traps only trigger for specific entities.
    /// </summary>
    [DataField]
    public EntityWhitelist? Whitelist;

    /// <summary>
    ///     If specified, entities matching this blacklist will NOT trigger the collision.
    ///     Use this to prevent specific entities from triggering traps (e.g., xenos not triggering their own traps).
    /// </summary>
    [DataField]
    public EntityWhitelist? Blacklist;
}
