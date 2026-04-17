using Content.Shared.Containers.ItemSlots;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._HL.Traits.Physical;
using Content.Shared.PowerCell.Components;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Tag;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._HL.Traits.Physical;

/// <summary>
/// Makes Big cyborgs use power cages instead of standard power cells.
/// </summary>
public sealed class BigCyborgPowerSourceSystem : EntitySystem
{
    private const string BigCyborgStartingPowerSource = "PowerCageMedium";
    private static readonly ProtoId<TagPrototype> PowerCageTag = "PowerCage";

    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly BatterySystem _battery = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BigWeaponHandlingComponent, ComponentAdd>(OnBigAdded);
        SubscribeLocalEvent<BigWeaponHandlingComponent, ComponentRemove>(OnBigRemoved);
    }

    private void OnBigAdded(EntityUid uid, BigWeaponHandlingComponent component, ComponentAdd args)
    {
        UpdateBigCyborgPowerSource(uid, ensurePowerCage: true);
    }

    private void OnBigRemoved(EntityUid uid, BigWeaponHandlingComponent component, ComponentRemove args)
    {
        if (!TryGetBorgCellSlot(uid, out var cellSlot, out var itemSlots))
            return;

        cellSlot.Whitelist = null;
        Dirty(uid, itemSlots);
    }

    private bool TryGetBorgCellSlot(EntityUid uid, out ItemSlot cellSlot, out ItemSlotsComponent itemSlots)
    {
        cellSlot = default!;
        itemSlots = default!;

        if (!HasComp<BorgChassisComponent>(uid)
            || !TryComp<PowerCellSlotComponent>(uid, out var powerSlot)
            || !TryComp<ItemSlotsComponent>(uid, out var slots)
            || !_itemSlots.TryGetSlot(uid, powerSlot.CellSlotId, out var slot, slots))
        {
            return false;
        }

        itemSlots = slots;
        cellSlot = slot!;

        return true;
    }

    private void UpdateBigCyborgPowerSource(EntityUid uid, bool ensurePowerCage)
    {
        if (!TryGetBorgCellSlot(uid, out var cellSlot, out var itemSlots))
        {
            return;
        }

        cellSlot.Whitelist = new EntityWhitelist
        {
            Tags = new() { PowerCageTag }
        };
        cellSlot.StartingItem = BigCyborgStartingPowerSource;
        Dirty(uid, itemSlots);

        if (!ensurePowerCage)
            return;

        if (cellSlot.Item is { } existing && _tag.HasTag(existing, PowerCageTag))
            return;

        if (cellSlot.Item is { } nonCage)
        {
            _container.RemoveEntity(uid, nonCage, force: true);
            QueueDel(nonCage);
        }

        if (cellSlot.ContainerSlot == null)
            return;

        var cage = Spawn(BigCyborgStartingPowerSource, Transform(uid).Coordinates);
        if (TryComp<BatteryComponent>(cage, out var battery))
            _battery.SetCharge(cage, battery.MaxCharge, battery);

        _container.Insert(cage, cellSlot.ContainerSlot);
    }
}
