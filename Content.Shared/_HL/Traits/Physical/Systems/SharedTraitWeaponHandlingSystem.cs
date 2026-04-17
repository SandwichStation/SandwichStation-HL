using System.Collections.Generic;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction.Events;
using Content.Shared.Item;
using Content.Shared.Popups;
using Content.Shared.Timing;
using Content.Shared.Tools.Components;
using Content.Shared.Whitelist;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Components;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._HL.Traits.Physical.Systems;

/// <summary>
/// Handles trait-driven weapon and hand restrictions (Small/Big/Tiny) that augment core wield behavior.
/// </summary>
public sealed class SharedTraitWeaponHandlingSystem : EntitySystem
{
    private const double BigGunUnwieldPenaltyFactor = 6.0;

    private readonly HashSet<(EntityUid Weapon, EntityUid User)> _smallActiveFireHolds = new();
    private readonly HashSet<(EntityUid Weapon, EntityUid User)> _smallShownFailureThisHold = new();

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedSmallPseudoWieldSystem _smallPseudoWield = default!;
    [Dependency] private readonly SharedGunSystem _gunSystem = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeAllEvent<RequestShootEvent>(OnRequestShoot);
        SubscribeAllEvent<RequestStopShootEvent>(OnRequestStopShoot);

        SubscribeLocalEvent<WieldableComponent, WieldAttemptEvent>(OnSmallWeaponWieldAttempt);

        SubscribeLocalEvent<MeleeRequiresWieldComponent, AttemptMeleeEvent>(OnMeleeAttempt);
        SubscribeLocalEvent<MeleeRequiresWieldComponent, WieldAttemptEvent>(OnRequiresWieldWieldAttempt);
        SubscribeLocalEvent<MeleeWeaponComponent, AttemptMeleeEvent>(OnGeneralMeleeAttempt);

        SubscribeLocalEvent<GunRequiresWieldComponent, ShotAttemptedEvent>(OnShootAttempt);
        SubscribeLocalEvent<GunRequiresWieldComponent, AttemptShootEvent>(OnAttemptShoot);
        SubscribeLocalEvent<GunRequiresWieldComponent, WieldAttemptEvent>(OnRequiresWieldWieldAttempt);

        SubscribeLocalEvent<GunComponent, ShotAttemptedEvent>(OnGeneralGunShootAttempt);
        SubscribeLocalEvent<GunComponent, AttemptShootEvent>(OnGeneralGunAttemptShoot);
        SubscribeLocalEvent<GunComponent, WieldAttemptEvent>(OnGunWieldAttempt);
        SubscribeLocalEvent<GunComponent, EntityTerminatingEvent>(OnGunTerminating);

        SubscribeLocalEvent<BigWeaponHandlingComponent, ComponentStartup>(OnBigStartup);
        SubscribeLocalEvent<BigWeaponHandlingComponent, Robust.Shared.Containers.EntInsertedIntoContainerMessage>(OnBigEntInserted);
        SubscribeLocalEvent<BigWeaponHandlingComponent, Robust.Shared.Containers.EntRemovedFromContainerMessage>(OnBigEntRemoved);
        SubscribeLocalEvent<BigWeaponHandlingComponent, ComponentShutdown>(OnBigShutdown);
    }

    private void OnBigStartup(EntityUid uid, BigWeaponHandlingComponent component, ComponentStartup args)
    {
        // If Big is added while already holding guns, container insert events do not re-fire.
        ApplyBigPenaltiesForHolder(uid);
    }

    private void OnBigShutdown(EntityUid uid, BigWeaponHandlingComponent component, ComponentShutdown args)
    {
        // Ensure all active Big penalties are reverted if the trait/component is removed.
        RemoveBigPenaltiesForHolder(uid);
    }

    private void OnBigEntInserted(EntityUid uid, BigWeaponHandlingComponent component, Robust.Shared.Containers.EntInsertedIntoContainerMessage args)
    {
        if (!_net.IsServer)
            return;

        ApplyBigPenaltyToGun(uid, args.Entity);
    }

    private void OnBigEntRemoved(EntityUid uid, BigWeaponHandlingComponent component, Robust.Shared.Containers.EntRemovedFromContainerMessage args)
    {
        if (!_net.IsServer)
            return;

        if (TryComp<GunComponent>(args.Entity, out var gun)
            && TryComp<HeldByBigWeaponHandlingComponent>(args.Entity, out var heldComp))
        {
            gun.MinAngle -= heldComp.MinAngleAdded;
            gun.AngleIncrease -= heldComp.AngleIncreaseAdded;
            gun.MaxAngle -= heldComp.MaxAngleAdded;
            _gunSystem.RefreshModifiers(args.Entity);
        }

        RemComp<HeldByBigWeaponHandlingComponent>(args.Entity);
    }

    private void OnGunTerminating(EntityUid uid, GunComponent component, ref EntityTerminatingEvent args)
    {
        // Avoid stale popup-throttle entries when guns are deleted mid hold.
        _smallActiveFireHolds.RemoveWhere(pair => pair.Weapon == uid);
        _smallShownFailureThisHold.RemoveWhere(pair => pair.Weapon == uid);
    }

    private void ApplyBigPenaltiesForHolder(EntityUid holder)
    {
        if (!TryComp<HandsComponent>(holder, out var hands))
            return;

        foreach (var hand in hands.Hands.Values)
        {
            if (hand.HeldEntity is not { } held)
                continue;

            ApplyBigPenaltyToGun(holder, held);
        }
    }

    private void RemoveBigPenaltiesForHolder(EntityUid holder)
    {
        var query = EntityQueryEnumerator<GunComponent, HeldByBigWeaponHandlingComponent>();
        while (query.MoveNext(out var gunUid, out var gun, out var held))
        {
            if (held.Holder != holder)
                continue;

            gun.MinAngle -= held.MinAngleAdded;
            gun.AngleIncrease -= held.AngleIncreaseAdded;
            gun.MaxAngle -= held.MaxAngleAdded;
            _gunSystem.RefreshModifiers(gunUid);
            RemComp<HeldByBigWeaponHandlingComponent>(gunUid);
        }
    }

    private void ApplyBigPenaltyToGun(EntityUid holder, EntityUid gunUid)
    {
        if (!TryComp<GunComponent>(gunUid, out var gun))
            return;

        // Keep guns that already require wielding on their normal behavior path.
        if (HasComp<GunRequiresWieldComponent>(gunUid))
            return;

        if (TryComp<HeldByBigWeaponHandlingComponent>(gunUid, out var existing) && existing.Holder == holder)
            return;

        if (existing != null)
        {
            // If the gun was somehow marked by another holder, restore first to avoid stacking.
            gun.MinAngle -= existing.MinAngleAdded;
            gun.AngleIncrease -= existing.AngleIncreaseAdded;
            gun.MaxAngle -= existing.MaxAngleAdded;
        }

        var heldComp = EnsureComp<HeldByBigWeaponHandlingComponent>(gunUid);
        heldComp.Holder = holder;
        heldComp.MinAngleAdded = gun.MinAngle * BigGunUnwieldPenaltyFactor;
        heldComp.AngleIncreaseAdded = gun.AngleIncrease * BigGunUnwieldPenaltyFactor;
        heldComp.MaxAngleAdded = gun.MaxAngle * BigGunUnwieldPenaltyFactor;

        gun.MinAngle += heldComp.MinAngleAdded;
        gun.AngleIncrease += heldComp.AngleIncreaseAdded;
        gun.MaxAngle += heldComp.MaxAngleAdded;
        _gunSystem.RefreshModifiers(gunUid);
    }

    private void OnMeleeAttempt(EntityUid uid, MeleeRequiresWieldComponent component, ref AttemptMeleeEvent args)
    {
        if (IsSmallHandlingUser(args.User))
        {
            args.Cancelled = true;
            args.Message = Loc.GetString("small-trait-cannot-use-wield-required", ("item", uid));
            return;
        }

        if (HasComp<BigWeaponHandlingComponent>(args.User))
            return;

        if (TryComp<WieldableComponent>(uid, out var wieldable) && !wieldable.Wielded)
        {
            args.Cancelled = true;
            args.Message = Loc.GetString("wieldable-component-requires", ("item", uid));
        }
    }

    private void OnGeneralMeleeAttempt(EntityUid uid, MeleeWeaponComponent component, ref AttemptMeleeEvent args)
    {
        if (HasComp<TinyWeaponHandlingComponent>(args.User) && HasComp<ToolComponent>(uid))
        {
            args.Cancelled = true;
            args.Message = Loc.GetString("tiny-trait-cannot-attack-with-tool", ("item", uid));
            return;
        }

        if (!IsSmallHandlingUser(args.User))
            return;

        if (IsSmallWeaponHandlingExcluded(uid, args.User))
            return;

        // Unarmed / innate attacks (hands, claws, etc.) are not item weapons and should remain usable.
        if (uid == args.User || !HasComp<ItemComponent>(uid))
            return;

        if (HasComp<MeleeRequiresWieldComponent>(uid))
            return;

        if (!TryComp<WieldableComponent>(uid, out var wieldable))
        {
            if (_smallPseudoWield.IsPseudoWielded(uid, args.User))
                return;

            args.Cancelled = true;
            args.Message = Loc.GetString("small-trait-must-wield-weapon", ("item", uid));
            return;
        }

        if (!wieldable.Wielded)
        {
            args.Cancelled = true;
            args.Message = Loc.GetString("wieldable-component-requires", ("item", uid));
        }
    }

    private void OnShootAttempt(EntityUid uid, GunRequiresWieldComponent component, ref ShotAttemptedEvent args)
    {
        if (IsSmallHandlingUser(args.User))
        {
            if (TryComp<WieldableComponent>(uid, out var smallWieldable) && !smallWieldable.Wielded)
            {
                args.Cancel();

                var now = _timing.CurTime;
                if (CanShowSmallFailurePopup(uid, args.User)
                    && now > component.LastPopup + component.PopupCooldown)
                {
                    component.LastPopup = now;
                    ShowSmallFailurePopup(Loc.GetString("wieldable-component-requires", ("item", uid)), args.Used, args.User);
                }

                return;
            }

            args.Cancel();

            var blockedNow = _timing.CurTime;
            if (CanShowSmallFailurePopup(uid, args.User)
                && blockedNow > component.LastPopup + component.PopupCooldown)
            {
                component.LastPopup = blockedNow;
                ShowSmallFailurePopup(Loc.GetString("small-trait-cannot-use-wield-required", ("item", uid)), args.Used, args.User);
            }

            return;
        }

        if (HasComp<BigWeaponHandlingComponent>(args.User))
            return;

        if (TryComp<WieldableComponent>(uid, out var wieldable) && !wieldable.Wielded)
        {
            args.Cancel();

            var time = _timing.CurTime;
            if (time > component.LastPopup + component.PopupCooldown &&
                !HasComp<MeleeWeaponComponent>(uid) &&
                !HasComp<MeleeRequiresWieldComponent>(uid))
            {
                component.LastPopup = time;
                var message = Loc.GetString("wieldable-component-requires", ("item", uid));
                _popup.PopupClient(message, args.Used, args.User);
            }
        }
    }

    private void OnGeneralGunShootAttempt(EntityUid uid, GunComponent component, ref ShotAttemptedEvent args)
    {
        if (!IsSmallHandlingUser(args.User))
            return;

        if (HasComp<GunRequiresWieldComponent>(uid))
            return;

        if (!TryComp<WieldableComponent>(uid, out var wieldable))
        {
            if (_smallPseudoWield.IsPseudoWielded(uid, args.User))
                return;

            args.Cancel();

            var now = _timing.CurTime;
            if (CanShowSmallFailurePopup(uid, args.User)
                && now > component.HandlingLastPopup + component.HandlingPopupCooldown)
            {
                component.HandlingLastPopup = now;
                ShowSmallFailurePopup(Loc.GetString("wieldable-component-requires", ("item", uid)), args.Used, args.User);
            }

            return;
        }

        if (!wieldable.Wielded)
        {
            args.Cancel();

            var now = _timing.CurTime;
            if (CanShowSmallFailurePopup(uid, args.User)
                && now > component.HandlingLastPopup + component.HandlingPopupCooldown)
            {
                component.HandlingLastPopup = now;
                ShowSmallFailurePopup(Loc.GetString("wieldable-component-requires", ("item", uid)), args.Used, args.User);
            }
        }
    }

    private void OnAttemptShoot(EntityUid uid, GunRequiresWieldComponent component, ref AttemptShootEvent args)
    {
        if (!IsSmallHandlingUser(args.User))
            return;

        if (TryComp<WieldableComponent>(uid, out var wieldable) && !wieldable.Wielded)
        {
            args.Cancelled = true;
            args.Message = Loc.GetString("wieldable-component-requires", ("item", uid));
            return;
        }

        args.Cancelled = true;
        args.Message = Loc.GetString("small-trait-cannot-use-wield-required", ("item", uid));
    }

    private void OnGeneralGunAttemptShoot(EntityUid uid, GunComponent component, ref AttemptShootEvent args)
    {
        if (!IsSmallHandlingUser(args.User) || HasComp<GunRequiresWieldComponent>(uid))
            return;

        if (!TryComp<WieldableComponent>(uid, out var wieldable))
        {
            if (_smallPseudoWield.IsPseudoWielded(uid, args.User))
                return;

            args.Cancelled = true;
            args.Message = Loc.GetString("wieldable-component-requires", ("item", uid));
            return;
        }

        if (!wieldable.Wielded)
        {
            args.Cancelled = true;
            args.Message = Loc.GetString("wieldable-component-requires", ("item", uid));
        }
    }

    private void OnGunWieldAttempt(EntityUid uid, GunComponent component, ref WieldAttemptEvent args)
    {
        if (!HasComp<BigWeaponHandlingComponent>(args.User))
            return;

        args.Cancel();

        var now = _timing.CurTime;
        if (now <= component.WieldFailLastPopup + component.WieldFailPopupCooldown)
            return;

        component.WieldFailLastPopup = now;

        // Keep this message explicit because the action fails by design for this trait.
        _popup.PopupClient(Loc.GetString("big-trait-cannot-wield-ranged"), args.User, args.User);
    }

    private void OnRequiresWieldWieldAttempt(EntityUid uid, GunRequiresWieldComponent component, ref WieldAttemptEvent args)
    {
        if (!IsSmallHandlingUser(args.User))
            return;

        args.Cancel();

        var now = _timing.CurTime;
        if (now <= component.LastPopup + component.PopupCooldown)
            return;

        component.LastPopup = now;

        _popup.PopupClient(Loc.GetString("small-trait-cannot-use-wield-required", ("item", uid)), args.User, args.User);
    }

    private void OnRequiresWieldWieldAttempt(EntityUid uid, MeleeRequiresWieldComponent component, ref WieldAttemptEvent args)
    {
        if (!IsSmallHandlingUser(args.User))
            return;

        args.Cancel();

        var now = _timing.CurTime;
        if (now <= component.LastPopup + component.PopupCooldown)
            return;

        component.LastPopup = now;

        _popup.PopupClient(Loc.GetString("small-trait-cannot-use-wield-required", ("item", uid)), args.User, args.User);
    }

    private void OnSmallWeaponWieldAttempt(EntityUid uid, WieldableComponent component, ref WieldAttemptEvent args)
    {
        if (!IsSmallHandlingUser(args.User))
            return;

        if (IsSmallWeaponHandlingExcluded(uid, args.User))
            return;

        // Only apply this to weapons; non-weapon wieldables should keep normal behavior.
        if (!HasComp<GunComponent>(uid) && !HasComp<MeleeWeaponComponent>(uid))
            return;

        // Keep the existing requires-wield-specific message for those special cases.
        if (HasComp<GunRequiresWieldComponent>(uid) || HasComp<MeleeRequiresWieldComponent>(uid))
            return;

        args.Cancel();

        var now = _timing.CurTime;
        if (now <= component.WieldFailLastPopup + component.WieldFailPopupCooldown)
            return;

        component.WieldFailLastPopup = now;

        _popup.PopupClient(Loc.GetString("small-trait-cannot-wield-weapon", ("item", uid)), args.User, args.User);
    }

    private void OnRequestShoot(RequestShootEvent ev, EntitySessionEventArgs args)
    {
        var user = args.SenderSession.AttachedEntity;
        if (user == null)
            return;

        var key = (GetEntity(ev.Gun), user.Value);

        // Long-session safety: ensure stale hold state from prior gun/request cycles
        // cannot suppress popup behavior indefinitely for this user.
        _smallActiveFireHolds.RemoveWhere(pair => pair.User == user.Value && pair.Weapon != key.Item1);
        _smallShownFailureThisHold.RemoveWhere(pair => pair.User == user.Value && pair.Weapon != key.Item1);

        _smallActiveFireHolds.Add(key);
        _smallShownFailureThisHold.Remove(key);
    }

    private void OnRequestStopShoot(RequestStopShootEvent ev, EntitySessionEventArgs args)
    {
        var user = args.SenderSession.AttachedEntity;
        if (user == null)
            return;

        var key = (GetEntity(ev.Gun), user.Value);
        _smallActiveFireHolds.Remove(key);
        _smallShownFailureThisHold.Remove(key);
    }

    private bool IsSmallHandlingUser(EntityUid user)
    {
        return HasComp<SmallWeaponHandlingComponent>(user);
    }

    private bool IsSmallWeaponHandlingExcluded(EntityUid item, EntityUid user)
    {
        if (!TryComp<SmallWeaponHandlingComponent>(user, out var smallHandling))
            return false;

        return _whitelist.IsWhitelistPass(smallHandling.WeaponHandlingExcluder, item);
    }

    private bool CanShowSmallFailurePopup(EntityUid weapon, EntityUid user)
    {
        var key = (weapon, user);

        // When fire is held, only show one failure popup per hold cycle.
        if (_smallActiveFireHolds.Contains(key))
        {
            if (_smallShownFailureThisHold.Contains(key))
                return false;

            _smallShownFailureThisHold.Add(key);
            return true;
        }

        // Non-hold invocation paths can still show a popup.
        return true;
    }

    private void ShowSmallFailurePopup(string message, EntityUid source, EntityUid user)
    {
        if (!_net.IsClient || !_timing.IsFirstTimePredicted)
            return;

        _popup.PopupPredicted(message, source, user);
    }
}
