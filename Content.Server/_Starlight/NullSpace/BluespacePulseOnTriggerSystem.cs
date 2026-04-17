using Content.Server.Explosion.EntitySystems;
using Content.Shared._Starlight.NullSpace;

namespace Content.Server._Starlight.NullSpace;

/// <summary>
/// Listens for TriggerEvent on entities with BluespacePulseOnTriggerComponent
/// and raises a BluespacePulseActionEvent to purge NullSpace in a radius.
/// </summary>
public sealed class BluespacePulseOnTriggerSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BluespacePulseOnTriggerComponent, TriggerEvent>(OnTrigger);
    }

    private void OnTrigger(Entity<BluespacePulseOnTriggerComponent> ent, ref TriggerEvent args)
    {
        var pulse = new BluespacePulseActionEvent
        {
            Radius = ent.Comp.Radius,
            StunSeconds = ent.Comp.StunSeconds,
            Performer = args.User ?? ent.Owner
        };

        RaiseLocalEvent(pulse);
        args.Handled = true;
    }
}
