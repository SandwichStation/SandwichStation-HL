using Content.Shared.FixedPoint;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server.Explosion.Components
{
    /// <summary>
    /// A component you can stick on a triggerable entity (for example a piece of clothing)
    /// that will pull reagents out of one of the entity's solutions and attempt to
    /// shove them into the body wearing/containing the item when the trigger fires.
    ///
    /// The implementation mirrors <see cref="ShockOnTriggerComponent" />; we use the
    /// container lookup to discover the entity wearing the item and then operate on
    /// the wearer's bloodstream.  The component itself only needs to know which
    /// solution on the item to draw from, how much to take each time and a cooldown
    /// so the effect can't spam repeatedly.
    /// </summary>
    [RegisterComponent, AutoGenerateComponentPause]
    [Access(typeof(Content.Server.Explosion.EntitySystems.TriggerSystem))]
    public sealed partial class InjectOnTriggerComponent : Component
    {
        /// <summary>
        /// Name of the solution on the owning entity that will be drained when the
        /// trigger fires.  The visible solution container on the clothing item should
        /// define this name (``default`` if you only have a single solution).
        /// </summary>
        [DataField, ViewVariables(VVAccess.ReadWrite)]
        public string Solution = "default";

        /// <summary>
        /// How much volume to remove from the solution each time the trigger fires.
        /// </summary>
        [DataField, ViewVariables(VVAccess.ReadWrite)]
        public FixedPoint2 Amount = FixedPoint2.New(1);

        /// <summary>
        /// Minimum time between successive injections.
        /// </summary>
        [DataField]
        public TimeSpan Cooldown = TimeSpan.FromSeconds(4);

        /// <summary>
        /// When the component is allowed to trigger again.  Managed by the system.
        /// </summary>
        [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
        [AutoPausedField]
        public TimeSpan NextTrigger = TimeSpan.Zero;
    }
}
