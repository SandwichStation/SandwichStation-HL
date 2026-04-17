using Content.Server.Actions;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Mobs.Components;
using Content.Server.Polymorph.Systems;
using Content.Shared._HL.Mobs;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Polymorph;
using Robust.Server.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server.Mobs;

public sealed class HLLivyathanSystem : EntitySystem
{
    private static readonly ProtoId<ReagentPrototype> BloodReagent = "Blood";
    private static readonly ProtoId<ReagentPrototype> AbyssalBloodReagent = "AbyssalBlood";

    [Dependency] private readonly ActionsSystem _actions = default!;
    [Dependency] private readonly BloodstreamSystem _bloodstream = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly PolymorphSystem _polymorph = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solution = default!;
    [Dependency] private readonly AudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HLLivyathanComponent, MapInitEvent>(OnMapInit, after: [typeof(BloodstreamSystem)]);
        SubscribeLocalEvent<HLLivyathanComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<HLLivyathanComponent, LivyathanDragonMorphActionEvent>(OnDragonMorphAction);
        SubscribeLocalEvent<HLLivyathanComponent, RevertPolymorphActionEvent>(OnDefaultRevertAction, before: [typeof(PolymorphSystem)]);
        SubscribeLocalEvent<HLLivyathanComponent, LivyathanDragonMorphDoAfterEvent>(OnDragonMorphDoAfter);
        SubscribeLocalEvent<HLLivyathanComponent, PolymorphedEvent>(OnPolymorphed);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<HLLivyathanComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.ActiveMorphDoAfter is not { } doAfterId)
                continue;

            if (_doAfter.IsRunning(doAfterId))
                continue;

            CleanupPortal((uid, comp));
            comp.ActiveMorphDoAfter = null;
        }
    }

    private void OnMapInit(Entity<HLLivyathanComponent> ent, ref MapInitEvent args)
    {
        if (TryComp<BloodstreamComponent>(ent, out var bloodstream)
            && _solution.ResolveSolution(ent.Owner, bloodstream.BloodSolutionName, ref bloodstream.BloodSolution, out var bloodSolution)
            && bloodstream.BloodSolution is { } bloodSolutionEntity)
        {
            var totalVolume = bloodSolution.MaxVolume;
            var firstHalf = totalVolume / 2;
            var secondHalf = totalVolume - firstHalf;
            var bloodData = _bloodstream.GetEntityBloodData(ent.Owner);

            _solution.RemoveAllSolution(bloodSolutionEntity);
            _solution.TryAddReagent(bloodSolutionEntity, BloodReagent, firstHalf, null, bloodData);
            _solution.TryAddReagent(bloodSolutionEntity, AbyssalBloodReagent, secondHalf, null, bloodData);
        }

        if (ent.Comp.AddDragonMorphAction)
            _actions.AddAction(ent, ref ent.Comp.DragonMorphActionEntity, ent.Comp.DragonMorphAction);
    }

    private void OnShutdown(Entity<HLLivyathanComponent> ent, ref ComponentShutdown args)
    {
        CleanupPortal(ent);
        ent.Comp.ActiveMorphDoAfter = null;
    }

    private void OnDragonMorphAction(Entity<HLLivyathanComponent> ent, ref LivyathanDragonMorphActionEvent args)
    {
        StartMorphDoAfter(ent, revert: false);
        args.Handled = true;
    }

    private void OnDefaultRevertAction(Entity<HLLivyathanComponent> ent, ref RevertPolymorphActionEvent args)
    {
        if (ent.Comp.SuppressRevertIntercept)
        {
            ent.Comp.SuppressRevertIntercept = false;
            return;
        }

        StartMorphDoAfter(ent, revert: true);
        args.Handled = true;
    }

    private void StartMorphDoAfter(Entity<HLLivyathanComponent> ent, bool revert)
    {
        if (ent.Comp.ActiveMorphDoAfter is { } activeDoAfter)
        {
            if (_doAfter.IsRunning(activeDoAfter))
                return;

            ent.Comp.ActiveMorphDoAfter = null;
            CleanupPortal(ent);
        }

        CleanupPortal(ent);

        var portal = Spawn(ent.Comp.DragonMorphPortalPrototype, Transform(ent).Coordinates);
        ent.Comp.ActivePortal = portal;

        var doAfterEvent = new LivyathanDragonMorphDoAfterEvent
        {
            Revert = revert,
        };

        var doAfterArgs = new DoAfterArgs(EntityManager, ent, ent.Comp.DragonMorphDoAfter, doAfterEvent, ent, target: ent, used: ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
        };

        if (!_doAfter.TryStartDoAfter(doAfterArgs, out var doAfterId))
        {
            CleanupPortal(ent);
            return;
        }

        ent.Comp.ActiveMorphDoAfter = doAfterId;
    }

    private void OnDragonMorphDoAfter(Entity<HLLivyathanComponent> ent, ref LivyathanDragonMorphDoAfterEvent args)
    {
        ent.Comp.ActiveMorphDoAfter = null;

        if (args.Cancelled)
        {
            CleanupPortal(ent);
            return;
        }

        if (args.Revert)
        {
            _audio.PlayPvs(ent.Comp.DragonPortalDepartureSound, ent);

            ent.Comp.SuppressRevertIntercept = true;
            var revertEvent = new RevertPolymorphActionEvent();
            RaiseLocalEvent(ent, revertEvent);

            CleanupPortal(ent);

            return;
        }

        _audio.PlayPvs(ent.Comp.DragonPortalDepartureSound, ent);
        _polymorph.PolymorphEntity(ent, ent.Comp.DragonPolymorphId);

        CleanupPortal(ent);
    }

    private void OnPolymorphed(Entity<HLLivyathanComponent> ent, ref PolymorphedEvent args)
    {
        if (!Exists(args.NewEntity))
            return;

        if (!TryComp<BloodstreamComponent>(ent.Owner, out var oldBloodstream)
            || !TryComp<BloodstreamComponent>(args.NewEntity, out var newBloodstream))
        {
            return;
        }

        TryApplyChemicalLevelPercentage(ent.Owner, oldBloodstream, args.NewEntity, newBloodstream);
        TryApplyBloodLevelPercentage(ent.Owner, oldBloodstream, args.NewEntity, newBloodstream);
        TryApplyBleedPercentage(oldBloodstream, newBloodstream, args.NewEntity);

        _audio.PlayPvs(ent.Comp.DragonPortalArrivalSound, args.NewEntity);
    }

    private void TryApplyBloodLevelPercentage(EntityUid oldEntity, BloodstreamComponent oldBloodstream, EntityUid newEntity, BloodstreamComponent newBloodstream)
    {
        if (!_solution.ResolveSolution(oldEntity, oldBloodstream.BloodSolutionName, ref oldBloodstream.BloodSolution, out var oldBloodSolution)
            || !_solution.ResolveSolution(newEntity, newBloodstream.BloodSolutionName, ref newBloodstream.BloodSolution, out var newBloodSolution)
            || newBloodstream.BloodSolution is not { } newBloodSolutionEntity)
        {
            return;
        }

        if (newBloodSolution.MaxVolume <= 0)
            return;

        var oldBloodPercent = Math.Clamp(oldBloodSolution.FillFraction, 0f, 1f);
        var targetBloodVolume = FixedPoint2.New(oldBloodPercent * newBloodSolution.MaxVolume.Float());
        var delta = targetBloodVolume - newBloodSolution.Volume;

        if (delta == 0)
            return;

        if (delta > 0)
        {
            _bloodstream.TryModifyBloodLevel(newEntity, delta, newBloodstream);
            return;
        }

        newBloodSolution.RemoveSolution(-delta);
        _solution.UpdateChemicals(newBloodSolutionEntity);
    }

    private void TryApplyChemicalLevelPercentage(EntityUid oldEntity, BloodstreamComponent oldBloodstream, EntityUid newEntity, BloodstreamComponent newBloodstream)
    {
        if (!_solution.ResolveSolution(oldEntity, oldBloodstream.ChemicalSolutionName, ref oldBloodstream.ChemicalSolution, out var oldChemicalSolution)
            || !_solution.ResolveSolution(newEntity, newBloodstream.ChemicalSolutionName, ref newBloodstream.ChemicalSolution, out var newChemicalSolution)
            || newBloodstream.ChemicalSolution is not { } newChemicalSolutionEntity)
        {
            return;
        }

        if (newChemicalSolution.MaxVolume <= 0)
            return;

        var oldChemicalPercent = Math.Clamp(oldChemicalSolution.FillFraction, 0f, 1f);
        var targetChemicalVolume = FixedPoint2.New(oldChemicalPercent * newChemicalSolution.MaxVolume.Float());

        _solution.RemoveAllSolution(newChemicalSolutionEntity);

        if (targetChemicalVolume > 0 && oldChemicalSolution.Volume > 0)
        {
            var scaledChemicals = oldChemicalSolution.Clone();
            var scale = targetChemicalVolume / scaledChemicals.Volume;
            scaledChemicals.ScaleSolution(scale.Float());

            _solution.TryAddSolution(newChemicalSolutionEntity, scaledChemicals);
        }

        _solution.UpdateChemicals(newChemicalSolutionEntity);
    }

    private void TryApplyBleedPercentage(BloodstreamComponent oldBloodstream, BloodstreamComponent newBloodstream, EntityUid newEntity)
    {
        if (oldBloodstream.MaxBleedAmount <= 0 || newBloodstream.MaxBleedAmount <= 0)
            return;

        var oldBleedPercent = oldBloodstream.BleedAmount / oldBloodstream.MaxBleedAmount;
        var targetBleed = Math.Clamp(oldBleedPercent * newBloodstream.MaxBleedAmount, 0f, newBloodstream.MaxBleedAmount);
        var delta = targetBleed - newBloodstream.BleedAmount;

        _bloodstream.TryModifyBleedAmount(newEntity, delta, newBloodstream);
    }

    private void CleanupPortal(Entity<HLLivyathanComponent> ent)
    {
        if (ent.Comp.ActivePortal is not { } portal)
            return;

        if (!TerminatingOrDeleted(portal))
            QueueDel(portal);

        ent.Comp.ActivePortal = null;
    }
}
