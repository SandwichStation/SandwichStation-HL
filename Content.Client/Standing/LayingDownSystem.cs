using Content.Client._DV.Abilities;
using Content.Shared.Buckle;
using Content.Shared.Rotation;
using Content.Shared.Standing;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Client.Standing;

public sealed class LayingDownSystem : SharedLayingDownSystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IEyeManager _eyeManager = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;
    [Dependency] private readonly AnimationPlayerSystem _animation = default!;
    [Dependency] private readonly SharedBuckleSystem _buckle = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!; // HardLight
    [Dependency] private readonly SharedTransformSystem _xform = default!; // HardLight

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LayingDownComponent, MoveEvent>(OnMovementInput);
        SubscribeNetworkEvent<CheckAutoGetUpEvent>(OnCheckAutoGetUp);
    }

    public override void Update(float frameTime)
    {
        // Update draw depth of laying down entities as necessary
        var query = EntityQueryEnumerator<LayingDownComponent, StandingStateComponent, SpriteComponent>(); // HardLight: Removed DrawDepthVisualizerComponent
        while (query.MoveNext(out var uid, out var layingDown, out var standing, out var sprite)) // HardLight: Removed out var drawDepth
        {
            // Do not modify the entities draw depth if it's modified externally
            if (sprite.DrawDepth != layingDown.NormalDrawDepth && sprite.DrawDepth != layingDown.CrawlingUnderDrawDepth)
                continue;

            // HardLight start
            var squeezeDepthOverride = TryComp<DrawDepthVisualizerComponent>(uid, out var drawDepth)
                && drawDepth.OriginalDrawDepth != null;

            var drawDepthTarget = (standing.CurrentState is StandingState.Lying && layingDown.IsCrawlingUnder || squeezeDepthOverride)
                ? layingDown.CrawlingUnderDrawDepth
                : layingDown.NormalDrawDepth;

            if (sprite.DrawDepth == drawDepthTarget)
                continue;

            _sprite.SetDrawDepth((uid, sprite), drawDepthTarget);
            // HardLight end
        }

        query.Dispose();
    }

    private void OnMovementInput(EntityUid uid, LayingDownComponent component, MoveEvent args)
    {
        if (!_timing.IsFirstTimePredicted
            || !_standing.IsDown(uid)
            || _buckle.IsBuckled(uid)
            || _animation.HasRunningAnimation(uid, "rotate")
            // || !TryComp<TransformComponent>(uid, out var transform) // HardLight
            || !TryComp<SpriteComponent>(uid, out var sprite)
            || !TryComp<RotationVisualsComponent>(uid, out var rotationVisuals))
            return;

        var rotation = _eyeManager.CurrentEye.Rotation + _xform.GetWorldRotation(Transform(uid));
        var targetRotation = rotation.GetDir() is Direction.SouthEast or Direction.East or Direction.NorthEast or Direction.North
            ? Angle.FromDegrees(270)
            : Angle.FromDegrees(90);

        if (rotationVisuals.HorizontalRotation == targetRotation && sprite.Rotation == targetRotation)
            return;

        rotationVisuals.HorizontalRotation = targetRotation;
        _sprite.SetRotation((uid, sprite), targetRotation);
    }

    private void OnCheckAutoGetUp(CheckAutoGetUpEvent ev, EntitySessionEventArgs args)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        var uid = GetEntity(ev.User);

        // HardLight start
        if (!Exists(uid) || !TryComp<RotationVisualsComponent>(uid, out var rotationVisuals))
            return;

        var rotation = _eyeManager.CurrentEye.Rotation + _xform.GetWorldRotation(Transform(uid));
        var targetRotation = rotation.GetDir() is Direction.SouthEast or Direction.East or Direction.NorthEast or Direction.North
            ? Angle.FromDegrees(270)
            : Angle.FromDegrees(90);

        if (rotationVisuals.HorizontalRotation == targetRotation)
            return;

        rotationVisuals.HorizontalRotation = targetRotation;
        // HardLight end
    }
}
