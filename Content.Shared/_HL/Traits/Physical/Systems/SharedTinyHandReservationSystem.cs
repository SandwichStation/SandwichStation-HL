using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Item;
using Content.Shared.Popups;
using Content.Shared.Whitelist;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Silicons.Borgs.Components;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._HL.Traits.Physical.Systems;

/// <summary>
/// Enforces Tiny hand-capacity behavior by requiring a reserved second hand for held items.
/// </summary>
public sealed class SharedTinyHandReservationSystem : EntitySystem
{
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TinyWeaponHandlingComponent, BeforeInteractHandEvent>(OnTinyBeforeInteractHand);
        SubscribeLocalEvent<TinyWeaponHandlingComponent, PickupAttemptEvent>(OnTinyPickupAttempt);
        SubscribeLocalEvent<ItemComponent, GotEquippedHandEvent>(OnTinyItemEquipped);
    }

    private void OnTinyBeforeInteractHand(Entity<TinyWeaponHandlingComponent> ent, ref BeforeInteractHandEvent args)
    {
        var item = args.Target;

        if (!TryGetTinyPickupBlockReason(ent, item, ent.Comp, out var popup))
            return;

        // Prevent SharedItemSystem from falling back to InteractionActivate on a failed pickup attempt.
        args.Handled = true;
        TryShowTinyPopup(ent, ent.Comp, popup, item);
    }

    private void OnTinyPickupAttempt(Entity<TinyWeaponHandlingComponent> ent, ref PickupAttemptEvent args)
    {
        var item = args.Item;

        if (!TryGetTinyPickupBlockReason(ent, item, ent.Comp, out var popup))
            return;

        args.Cancel();
        TryShowTinyPopup(ent, ent.Comp, popup, item);
    }

    private bool TryGetTinyPickupBlockReason(EntityUid user, EntityUid item, TinyWeaponHandlingComponent comp, out string popup)
    {
        popup = string.Empty;

        if (!HasComp<ItemComponent>(item))
            return false;

        if ((HasComp<GunComponent>(item) || HasComp<MeleeWeaponComponent>(item))
            && !IsTinyWeaponPickupExcluded(item, comp))
        {
            popup = Loc.GetString("tiny-trait-cannot-pickup-weapon", ("item", item));
            return true;
        }

        // Borg chassis keep Tiny weapon bans but are exempt from one-hand-to-two-hand reservation.
        if (IsBorgChassis(user))
            return false;

        if (!TryComp<HandsComponent>(user, out var hands))
            return false;

        if (CountTinyRealHeldItems(user, hands) < 1)
            return false;

        popup = Loc.GetString("tiny-trait-requires-two-hands", ("item", item));
        return true;
    }

    private bool IsTinyWeaponPickupExcluded(EntityUid item, TinyWeaponHandlingComponent comp)
    {
        return _whitelist.IsWhitelistPass(comp.PickupWeaponExcluder, item);
    }

    private void OnTinyItemEquipped(EntityUid uid, ItemComponent component, ref GotEquippedHandEvent args)
    {
        if (_net.IsClient)
            return;

        if (!HasComp<TinyWeaponHandlingComponent>(args.User))
            return;

        if (!TryComp<HandsComponent>(args.User, out var hands))
            return;

        if (IsBorgChassis(args.User))
            return;

        // Tiny is only allowed to hold one real hand item at a time.
        if (CountTinyRealHeldItems(args.User, hands) <= 1)
            return;

        if (TryComp<TinyWeaponHandlingComponent>(args.User, out var tinyComp))
            TryShowTinyPopup(args.User, tinyComp, Loc.GetString("tiny-trait-requires-two-hands", ("item", uid)), uid);

        _hands.TryDrop(args.User, args.Hand, checkActionBlocker: false);
    }

    private bool IsBorgChassis(EntityUid uid)
    {
        return HasComp<BorgChassisComponent>(uid);
    }

    private void TryShowTinyPopup(EntityUid user, TinyWeaponHandlingComponent tinyComp, string message, EntityUid source)
    {
        var now = _timing.CurTime;
        if (now <= tinyComp.LastPopup + tinyComp.PopupCooldown)
            return;

        tinyComp.LastPopup = now;

        if (_net.IsClient)
        {
            if (_timing.IsFirstTimePredicted)
                _popup.PopupPredicted(message, source, user);

            return;
        }

        _popup.PopupClient(message, source, user);
    }

    private int CountTinyRealHeldItems(EntityUid user, HandsComponent hands)
    {
        var count = 0;

        foreach (var hand in hands.Hands.Values)
        {
            if (hand.HeldEntity is not { } held)
                continue;

            if (!HasComp<ItemComponent>(held) || HasComp<VirtualItemComponent>(held))
                continue;

            count++;
        }

        return count;
    }
}
