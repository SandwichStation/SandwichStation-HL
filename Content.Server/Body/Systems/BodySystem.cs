using Content.Server.Body.Components;
using Content.Server.Ghost;
using Content.Server.Humanoid;
using Content.Shared._Shitmed.Body.Part;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Humanoid;
using Content.Shared.Mind;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Timing;
using System.Numerics;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Prototypes;

// Shitmed Change
using System.Linq;
using Content.Shared.Damage;
using Content.Shared.Gibbing.Events;
using Content.Shared._HL.Fire;

namespace Content.Server.Body.Systems;

public sealed class BodySystem : SharedBodySystem
{
    [Dependency] private readonly GhostSystem _ghostSystem = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly HumanoidAppearanceSystem _humanoidSystem = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!; // Shitmed Change
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedMindSystem _mindSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BodyComponent, MoveInputEvent>(OnRelayMoveInput);
        SubscribeLocalEvent<BodyComponent, ApplyMetabolicMultiplierEvent>(OnApplyMetabolicMultiplier);
    }

    private void OnRelayMoveInput(Entity<BodyComponent> ent, ref MoveInputEvent args)
    {
        // If they haven't actually moved then ignore it.
        if ((args.Entity.Comp.HeldMoveButtons &
             (MoveButtons.Down | MoveButtons.Left | MoveButtons.Up | MoveButtons.Right)) == 0x0)
        {
            return;
        }

        if (_mobState.IsDead(ent) && _mindSystem.TryGetMind(ent, out var mindId, out var mind))
        {
            // mind.TimeOfDeath ??= _gameTiming.RealTime;
            mind.TimeOfDeath ??= _gameTiming.CurTime; // Frontier - fix returning to body messing with the your TOD
            _ghostSystem.OnGhostAttempt(mindId, canReturnGlobal: true, mind: mind);
        }
    }

    public bool ReplaceBodyPart(
        EntityUid bodyId,
        BodyPartType partType,
        BodyPartSymmetry symmetry,
        EntProtoId replacementProto,
        BodyComponent? body = null)
    {
        if (!Resolve(bodyId, ref body, logMissing: false))
            return false;

        foreach (var (partUid, partComp) in GetBodyChildren(bodyId, body))
        {
            if (partComp.PartType != partType)
                continue;

            if (symmetry != BodyPartSymmetry.None && partComp.Symmetry != symmetry)
                continue;

            var parentSlot = GetParentPartAndSlotOrNull(partUid);
            if (parentSlot is null)
                return false;

            var slotId = parentSlot.Value.Slot;

            if (!Containers.TryGetContainer(parentSlot.Value.Parent, GetPartSlotContainerId(slotId), out var container))
                return false;

            // Remove existing part from the container to trigger removal flow
            Containers.Remove(partUid, container);

            // Spawn and attach the replacement
            var newPart = Spawn(replacementProto, new EntityCoordinates(parentSlot.Value.Parent, Vector2.Zero));
            if (!TryComp(newPart, out BodyPartComponent? newPartComp))
                return false;

            return AttachPart(parentSlot.Value.Parent, slotId, newPart, null, newPartComp);
        }

        return false;
    }

    public bool ReplaceOrInsertBodyPart(
        EntityUid bodyId,
        BodyPartType partType,
        BodyPartSymmetry symmetry,
        EntProtoId replacementProto,
        bool replaceIfPresent,
        BodyComponent? body = null)
    {
        if (!Resolve(bodyId, ref body, logMissing: false))
            return false;

        if (replaceIfPresent)
        {
            if (ReplaceBodyPart(bodyId, partType, symmetry, replacementProto, body))
                return true;
        }
        else
        {
            // If we shouldn't replace, bail if a matching part already exists.
            if (GetBodyChildrenOfType(bodyId, partType, body, symmetry).Any())
                return false;
        }

        // Try to insert into an empty slot of the desired type (and symmetry if specified).
        string? desiredSym = symmetry == BodyPartSymmetry.None ? null : symmetry.ToString().ToLowerInvariant();

        // Walk all parts to find a parent that has a suitable empty child slot.
        foreach (var (parentId, parentComp) in GetBodyChildren(bodyId, body))
        {
            foreach (var (slotId, slot) in parentComp.Children)
            {
                if (slot.Type != partType)
                    continue;

                if (desiredSym != null && !slotId.ToLowerInvariant().Contains(desiredSym))
                    continue;

                if (!Containers.TryGetContainer(parentId, GetPartSlotContainerId(slotId), out var container))
                    continue;

                if (container.ContainedEntities.Count != 0)
                    continue;

                var newPart = Spawn(replacementProto, new EntityCoordinates(parentId, Vector2.Zero));
                if (!TryComp(newPart, out BodyPartComponent? newPartComp))
                    return false;

                return AttachPart(parentId, slotId, newPart, parentComp, newPartComp);
            }
        }

        return false;
    }

    private void OnApplyMetabolicMultiplier(
        Entity<BodyComponent> ent,
        ref ApplyMetabolicMultiplierEvent args)
    {
        foreach (var organ in GetBodyOrgans(ent, ent))
        {
            RaiseLocalEvent(organ.Id, ref args);
        }
    }

    protected override void AddPart(
        Entity<BodyComponent?> bodyEnt,
        Entity<BodyPartComponent> partEnt,
        string slotId)
    {
        // TODO: Predict this probably.
        base.AddPart(bodyEnt, partEnt, slotId);

        if (TryComp<HumanoidAppearanceComponent>(bodyEnt, out var humanoid))
        {
            var layer = partEnt.Comp.ToHumanoidLayers();
            if (layer != null)
            {
                var layers = HumanoidVisualLayersExtension.Sublayers(layer.Value);
                _humanoidSystem.SetLayersVisibility(
                    (bodyEnt, humanoid), new[] { layer.Value }, true); // Shitmed Change
            }
        }
    }

    protected override void RemovePart(
        Entity<BodyComponent?> bodyEnt,
        Entity<BodyPartComponent> partEnt,
        string slotId)
    {
        base.RemovePart(bodyEnt, partEnt, slotId);

        if (!TryComp<HumanoidAppearanceComponent>(bodyEnt, out var humanoid))
            return;

        var layer = partEnt.Comp.ToHumanoidLayers();

        if (layer is null)
            return;

        var layers = HumanoidVisualLayersExtension.Sublayers(layer.Value);
        _humanoidSystem.SetLayersVisibility((bodyEnt, humanoid), layers, visible: false);
        _appearance.SetData(bodyEnt, layer, true); // Shitmed Change
    }

    public override HashSet<EntityUid> GibBody(
        EntityUid bodyId,
        bool gibOrgans = false,
        BodyComponent? body = null,
        bool launchGibs = true,
        Vector2? splatDirection = null,
        float splatModifier = 1,
        Angle splatCone = default,
        SoundSpecifier? gibSoundOverride = null,
        // Shitmed Change
        GibType gib = GibType.Gib,
        GibContentsOption contents = GibContentsOption.Drop)
    {
        if (!Resolve(bodyId, ref body, logMissing: false)
            || TerminatingOrDeleted(bodyId)
            || EntityManager.IsQueuedForDeletion(bodyId))
        {
            return new HashSet<EntityUid>();
        }

        var xform = Transform(bodyId);
        if (xform.MapUid is null)
            return new HashSet<EntityUid>();

        var beforeEv = new BeforeGibbedEvent(bodyId); // Frontier: before gibbed event
        RaiseLocalEvent(bodyId, ref beforeEv); // Frontier: before gibbed event

        var gibs = base.GibBody(bodyId, gibOrgans, body, launchGibs: launchGibs,
            splatDirection: splatDirection, splatModifier: splatModifier, splatCone: splatCone,
            gib: gib, contents: contents); // Shitmed Change

        var ev = new BeingGibbedEvent(gibs);
        RaiseLocalEvent(bodyId, ref ev);

        QueueDel(bodyId);

        return gibs;
    }

    // Shitmed Change Start
    public override HashSet<EntityUid> GibPart(
        EntityUid partId,
        BodyPartComponent? part = null,
        bool launchGibs = true,
        Vector2? splatDirection = null,
        float splatModifier = 1,
        Angle splatCone = default,
        SoundSpecifier? gibSoundOverride = null)
    {
        if (!Resolve(partId, ref part, logMissing: false)
            || TerminatingOrDeleted(partId)
            || EntityManager.IsQueuedForDeletion(partId))
            return new HashSet<EntityUid>();

        if (Transform(partId).MapUid is null)
            return new HashSet<EntityUid>();

        var gibs = base.GibPart(partId, part, launchGibs: launchGibs,
            splatDirection: splatDirection, splatModifier: splatModifier, splatCone: splatCone);

        var ev = new BeingGibbedEvent(gibs);
        RaiseLocalEvent(partId, ref ev);

        if (gibs.Any())
            QueueDel(partId);

        return gibs;
    }

    public override bool BurnPart(EntityUid partId, BodyPartComponent? part = null)
    {
        if (!Resolve(partId, ref part, logMissing: false)
            || TerminatingOrDeleted(partId)
            || EntityManager.IsQueuedForDeletion(partId))
            return false;

        // Fireproof trait: prevent body parts from burning off while on fire.
        if (part.Body is { } bodyEnt && HasComp<FireproofBodyPartsComponent>(bodyEnt))
            return false;

        return base.BurnPart(partId, part);
    }

    protected override void ApplyPartMarkings(EntityUid target, BodyPartAppearanceComponent component)
    {
        return;
    }

    protected override void RemoveBodyMarkings(EntityUid target, BodyPartAppearanceComponent partAppearance, HumanoidAppearanceComponent bodyAppearance)
    {
        foreach (var (visualLayer, markingList) in partAppearance.Markings)
            foreach (var marking in markingList)
                _humanoidSystem.RemoveMarking(target, marking.MarkingId, sync: false, humanoid: bodyAppearance);

        Dirty(target, bodyAppearance);
    }

    // Shitmed Change End
}
