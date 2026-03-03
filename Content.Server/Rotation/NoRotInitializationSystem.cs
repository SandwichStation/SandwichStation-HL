using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.Server.Rotation;

/// <summary>
/// Ensures entities with NoLocalRotation never keep stale non-zero local rotations
/// when spawned or loaded from map data.
/// </summary>
public sealed class NoRotInitializationSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TransformComponent, MapInitEvent>(OnTransformInit);
        SubscribeLocalEvent<TransformStartupEvent>(OnTransformStartup);
    }

    private void OnTransformInit(EntityUid uid, TransformComponent component, MapInitEvent args)
    {
        EnsureZeroRotation(uid, component);
    }

    private void OnTransformStartup(ref TransformStartupEvent ev)
    {
        EnsureZeroRotation(ev.Entity.Owner, ev.Entity.Comp);
    }

    private void EnsureZeroRotation(EntityUid uid, TransformComponent component)
    {
        if (!component.NoLocalRotation || component.LocalRotation.EqualsApprox(Angle.Zero))
            return;

        component.NoLocalRotation = false;
        _transform.SetLocalRotation(uid, Angle.Zero, component);
        component.NoLocalRotation = true;
    }
}
