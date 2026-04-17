using System.Linq;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Popups;
using Content.Shared.Timing;
using Content.Shared.Whitelist;
using Content.Shared._HL.Traits.Physical;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Wieldable.Components;

namespace Content.Shared._HL.Traits.Physical.Systems;

/// <summary>
/// Handles Small-trait pseudo-wield for non-wieldable weapons by reserving an extra hand via virtual items.
/// </summary>
public sealed class SharedSmallPseudoWieldSystem : EntitySystem
{
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedVirtualItemSystem _virtualItem = default!;
    [Dependency] private readonly UseDelaySystem _delay = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GunComponent, UseInHandEvent>(OnSmallPseudoWieldUseInHand, before: [typeof(SharedGunSystem), typeof(BatteryWeaponFireModesSystem)]);
        SubscribeLocalEvent<MeleeWeaponComponent, UseInHandEvent>(OnSmallPseudoWieldUseInHand, before: [typeof(SharedGunSystem), typeof(BatteryWeaponFireModesSystem)]);
        SubscribeLocalEvent<GunComponent, GotUnequippedHandEvent>(OnSmallPseudoWieldLeaveHand);
        SubscribeLocalEvent<MeleeWeaponComponent, GotUnequippedHandEvent>(OnSmallPseudoWieldLeaveHand);
        SubscribeLocalEvent<GunComponent, HandDeselectedEvent>(OnSmallPseudoWieldDeselected);
        SubscribeLocalEvent<MeleeWeaponComponent, HandDeselectedEvent>(OnSmallPseudoWieldDeselected);
    }

    public bool IsPseudoWielded(EntityUid item, EntityUid user)
    {
        foreach (var hand in _hands.EnumerateHands(user))
        {
            if (!TryComp<VirtualItemComponent>(hand.HeldEntity, out var virtualItem))
                continue;

            if (virtualItem.BlockingEntity == item)
                return true;
        }

        return false;
    }

    private void OnSmallPseudoWieldUseInHand(EntityUid uid, GunComponent component, UseInHandEvent args)
    {
        if (args.Handled || !IsSmallHandlingUser(args.User) || HasComp<WieldableComponent>(uid))
            return;

        if (IsSmallWeaponHandlingExcluded(uid, args.User))
            return;

        if (IsPseudoWielded(uid, args.User))
            return;

        if (TryWieldSmallPseudo(uid, args.User))
            _popup.PopupClient(Loc.GetString("wieldable-component-successful-wield", ("item", uid)), args.User, args.User);

        args.Handled = true;
    }

    private void OnSmallPseudoWieldUseInHand(EntityUid uid, MeleeWeaponComponent component, UseInHandEvent args)
    {
        if (args.Handled || !IsSmallHandlingUser(args.User) || HasComp<WieldableComponent>(uid))
            return;

        if (IsSmallWeaponHandlingExcluded(uid, args.User))
            return;

        if (IsPseudoWielded(uid, args.User))
            return;

        if (TryWieldSmallPseudo(uid, args.User))
            _popup.PopupClient(Loc.GetString("wieldable-component-successful-wield", ("item", uid)), args.User, args.User);

        args.Handled = true;
    }

    private void OnSmallPseudoWieldLeaveHand(EntityUid uid, GunComponent component, GotUnequippedHandEvent args)
    {
        if (uid != args.Unequipped)
            return;

        UnwieldSmallPseudo(uid, args.User, force: true);
    }

    private void OnSmallPseudoWieldLeaveHand(EntityUid uid, MeleeWeaponComponent component, GotUnequippedHandEvent args)
    {
        if (uid != args.Unequipped)
            return;

        UnwieldSmallPseudo(uid, args.User, force: true);
    }

    private void OnSmallPseudoWieldDeselected(EntityUid uid, GunComponent component, HandDeselectedEvent args)
    {
        if (_hands.EnumerateHands(args.User).Count() > 2)
            return;

        UnwieldSmallPseudo(uid, args.User);
    }

    private void OnSmallPseudoWieldDeselected(EntityUid uid, MeleeWeaponComponent component, HandDeselectedEvent args)
    {
        if (_hands.EnumerateHands(args.User).Count() > 2)
            return;

        UnwieldSmallPseudo(uid, args.User);
    }

    private bool TryWieldSmallPseudo(EntityUid item, EntityUid user)
    {
        if (!_hands.IsHolding(user, item))
            return false;

        if (IsPseudoWielded(item, user))
            return true;

        if (TryComp(item, out UseDelayComponent? useDelay) && !_delay.TryResetDelay((item, useDelay), true))
            return false;

        if (_virtualItem.TrySpawnVirtualItemInHand(item, user, out _))
            return true;

        _popup.PopupClient(Loc.GetString("wieldable-component-not-enough-free-hands", ("number", 1), ("item", item)), user, user);
        return false;
    }

    private void UnwieldSmallPseudo(EntityUid item, EntityUid user, bool force = false)
    {
        if (!IsPseudoWielded(item, user))
            return;

        _virtualItem.DeleteInHandsMatching(user, item);

        // Match normal wield UX: manual unwield paths show feedback, forced unwields stay silent.
        if (!force)
            _popup.PopupClient(Loc.GetString("wieldable-component-failed-wield", ("item", item)), user, user);
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
}
