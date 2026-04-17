using Content.Shared.Actions;
using Content.Shared.Climbing.Components;
using Content.Shared.Climbing.Events;
// using Content.Shared.Maps; // HardLight
using Content.Shared.Mobs;
using Content.Shared.Movement.Systems;
// using Content.Shared.Physics; // HardLight
using Content.Shared.Popups;
using Content.Shared.Standing;
using Robust.Shared.Network; // HardLight
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing; // HardLight

namespace Content.Shared._DV.Abilities;

/// <summary>
/// Not to be confused with laying down, <see cref="CrawlUnderObjectsComponent"/> lets you move under tables.
/// </summary>
public sealed partial class CrawlUnderObjectsSystem : EntitySystem // HardLight: Added partial
{
    [Dependency] private readonly MovementSpeedModifierSystem _moveSpeed = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;
    [Dependency] private readonly INetManager _net = default!; // HardLight
    [Dependency] private readonly IGameTiming _timing = default!; // HardLight

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CrawlUnderObjectsComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<CrawlUnderObjectsComponent, ComponentStartup>(OnStartup); // HardLight
        SubscribeLocalEvent<CrawlUnderObjectsComponent, ToggleCrawlingStateEvent>(OnToggleCrawling);
        SubscribeLocalEvent<CrawlUnderObjectsComponent, AttemptClimbEvent>(OnAttemptClimb);
        SubscribeLocalEvent<CrawlUnderObjectsComponent, DownAttemptEvent>(CancelWhenSneaking);
        SubscribeLocalEvent<CrawlUnderObjectsComponent, StandAttemptEvent>(CancelWhenSneaking);
        SubscribeLocalEvent<CrawlUnderObjectsComponent, DownedEvent>(OnDowned); // HardLight
        SubscribeLocalEvent<CrawlUnderObjectsComponent, StoodEvent>(OnStood); // HardLight
        SubscribeLocalEvent<CrawlUnderObjectsComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMoveSpeed);
        SubscribeLocalEvent<CrawlUnderObjectsComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<FixturesComponent, ComponentStartup>(OnFixturesStartup); // HardLight

        SubscribeLocalEvent<FixturesComponent, CrawlingUpdatedEvent>(OnCrawlingUpdated);
    }

    private void OnMapInit(Entity<CrawlUnderObjectsComponent> ent, ref MapInitEvent args)
    {
        EnsureToggleAction(ent); // HardLight
    }

    private void OnFixturesStartup(Entity<FixturesComponent> ent, ref ComponentStartup args) // HardLight
    {
        if (!TryComp<CrawlUnderObjectsComponent>(ent, out var crawl))
            return;

        EnsureBaselineInflation((ent.Owner, crawl));
    }

    // HardLight start
    private void OnStartup(Entity<CrawlUnderObjectsComponent> ent, ref ComponentStartup args)
    {
        EnsureToggleAction(ent);
        EnsureBaselineInflation(ent);
    }

    private void EnsureToggleAction(Entity<CrawlUnderObjectsComponent> ent)
    {
        if (ent.Comp.ActionProto == null)
            return;

        if (ent.Comp.ToggleHideAction is { } existing && existing != EntityUid.Invalid)
            return;

        _actions.AddAction(ent, ref ent.Comp.ToggleHideAction, ent.Comp.ActionProto.Value);
    }
    // HardLight end

    private void OnToggleCrawling(Entity<CrawlUnderObjectsComponent> ent, ref ToggleCrawlingStateEvent args)
    {
        if (_net.IsClient && !_timing.IsFirstTimePredicted) // HardLight
            return;

        if (args.Handled)
            return;

        args.Handled = TryToggle(ent);
    }

    private void OnAttemptClimb(Entity<CrawlUnderObjectsComponent> ent, ref AttemptClimbEvent args)
    {
        if (ent.Comp.Enabled)
            args.Cancelled = true;
    }

    private void CancelWhenSneaking<TEvent>(Entity<CrawlUnderObjectsComponent> ent, ref TEvent args) where TEvent : CancellableEntityEventArgs
    {
        if (ent.Comp.Enabled)
            args.Cancel();
    }

    private void OnRefreshMoveSpeed(Entity<CrawlUnderObjectsComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.Enabled && !_standing.IsDown(ent)) // HardLight: Added !_standing.IsDown(ent)
            args.ModifySpeed(ent.Comp.SneakSpeedModifier, ent.Comp.SneakSpeedModifier);
    }

    private void OnMobStateChanged(Entity<CrawlUnderObjectsComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.OldMobState != MobState.Alive || !ent.Comp.Enabled)
            return;

        // crawling prevents downing, so when you go crit/die stop crawling and force downing
        SetEnabled(ent, false);
        _standing.Down(ent);
    }

    /// <summary>
    /// Tries to enable or disable sneaking
    /// </summary>
    public bool TrySetEnabled(Entity<CrawlUnderObjectsComponent> ent, bool enabled)
    {
        // HardLight start
        if (ent.Comp.Enabled == enabled)
            return false;

        // Always allow disabling so users cannot get stuck in squeeze mode due to state checks.
        if (enabled)
        {
            EnsureBaselineInflation(ent);

            if (_standing.IsDown(ent))
                return false;

            if (TryComp<ClimbingComponent>(ent, out var climbing) && climbing.IsClimbing)
                return false;
        }
        // HardLight end

        SetEnabled(ent, enabled);

        var msg = Loc.GetString("crawl-under-objects-toggle-" + (enabled ? "on" : "off"));
        _popup.PopupPredicted(msg, ent, ent);

        return true;
    }

    private void SetEnabled(Entity<CrawlUnderObjectsComponent> ent, bool enabled)
    {
        ent.Comp.Enabled = enabled;

        // HardLight: Apply fixture geometry changes first so movement prediction uses the updated hitbox in the same tick.
        var ev = new CrawlingUpdatedEvent(enabled, ent.Comp);
        RaiseLocalEvent(ent, ref ev);

        // HardLight start
        _moveSpeed.RefreshMovementSpeedModifiers(ent);
        _appearance.SetData(ent, SneakingVisuals.Sneaking, enabled);
        Dirty(ent);
        // HardLight end
    }

    /// <summary>
    /// Tries to toggle sneaking
    /// </summary>
    public bool TryToggle(Entity<CrawlUnderObjectsComponent> ent)
    {
        return TrySetEnabled(ent, !ent.Comp.Enabled);
    }
}
