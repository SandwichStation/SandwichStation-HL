using Content.Server.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Administration.Logs;
using Content.Shared.Body.Organ;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Database;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Server.Mobs.Components; // HardLight
using Robust.Shared.Collections;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Content.Server._Funkystation.Genetics.Mutations.Components;

namespace Content.Server.Body.Systems
{
    public sealed class MetabolizerSystem : EntitySystem
    {
        [Dependency] private readonly IGameTiming _gameTiming = default!;
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
        [Dependency] private readonly IRobustRandom _random = default!;
        [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;
        [Dependency] private readonly MobStateSystem _mobStateSystem = default!;
        [Dependency] private readonly SharedSolutionContainerSystem _solutionContainerSystem = default!;

        private EntityQuery<OrganComponent> _organQuery;
        private EntityQuery<SolutionContainerManagerComponent> _solutionQuery;

        public override void Initialize()
        {
            base.Initialize();

            _organQuery = GetEntityQuery<OrganComponent>();
            _solutionQuery = GetEntityQuery<SolutionContainerManagerComponent>();

            SubscribeLocalEvent<MetabolizerComponent, ComponentInit>(OnMetabolizerInit);
            SubscribeLocalEvent<MetabolizerComponent, MapInitEvent>(OnMapInit);
            SubscribeLocalEvent<MetabolizerComponent, EntityUnpausedEvent>(OnUnpaused);
            SubscribeLocalEvent<MetabolizerComponent, ApplyMetabolicMultiplierEvent>(OnApplyMetabolicMultiplier);
        }

        private void OnMapInit(Entity<MetabolizerComponent> ent, ref MapInitEvent args)
        {
            ent.Comp.NextUpdate = _gameTiming.CurTime + ent.Comp.UpdateInterval;
        }

        private void OnUnpaused(Entity<MetabolizerComponent> ent, ref EntityUnpausedEvent args)
        {
            ent.Comp.NextUpdate += args.PausedTime;
        }

        private void OnMetabolizerInit(Entity<MetabolizerComponent> entity, ref ComponentInit args)
        {
            if (!entity.Comp.SolutionOnBody)
            {
                _solutionContainerSystem.EnsureSolution(entity.Owner, entity.Comp.SolutionName, out _);
            }
            else if (_organQuery.CompOrNull(entity)?.Body is { } body)
            {
                _solutionContainerSystem.EnsureSolution(body, entity.Comp.SolutionName, out _);
            }
        }

        private void OnApplyMetabolicMultiplier(
            Entity<MetabolizerComponent> ent,
            ref ApplyMetabolicMultiplierEvent args)
        {
            if (!float.IsFinite(args.Multiplier) || args.Multiplier == 0f)
                return;

            // TODO REFACTOR THIS
            // This will slowly drift over time due to floating point errors.
            // Instead, raise an event with the base rates and allow modifiers to get applied to it.
            if (args.Apply)
            {
                ent.Comp.UpdateInterval *= args.Multiplier;
                return;
            }

            ent.Comp.UpdateInterval /= args.Multiplier;
        }

        public override void Update(float frameTime)
        {
            base.Update(frameTime);

            var metabolizers = new ValueList<(EntityUid Uid, MetabolizerComponent Component)>(Count<MetabolizerComponent>());
            var query = EntityQueryEnumerator<MetabolizerComponent>();

            while (query.MoveNext(out var uid, out var comp))
            {
                metabolizers.Add((uid, comp));
            }

            foreach (var (uid, metab) in metabolizers)
            {
                // Only update as frequently as it should
                if (_gameTiming.CurTime < metab.NextUpdate)
                    continue;

                metab.NextUpdate += metab.UpdateInterval;
                TryMetabolize((uid, metab));
            }
        }

        private void TryMetabolize(Entity<MetabolizerComponent, OrganComponent?, SolutionContainerManagerComponent?> ent)
        {
            _organQuery.Resolve(ent, ref ent.Comp2, logMissing: false);

            // First step is get the solution we actually care about
            var solutionName = ent.Comp1.SolutionName;
            Solution? solution = null;
            Entity<SolutionComponent>? soln = default!;
            EntityUid? solutionEntityUid = null;

            if (ent.Comp1.SolutionOnBody)
            {
                if (ent.Comp2?.Body is { } body)
                {
                    if (!_solutionQuery.Resolve(body, ref ent.Comp3, logMissing: false))
                        return;

                    _solutionContainerSystem.TryGetSolution((body, ent.Comp3), solutionName, out soln, out solution);
                    solutionEntityUid = body;
                }
            }
            else
            {
                if (!_solutionQuery.Resolve(ent, ref ent.Comp3, logMissing: false))
                    return;

                _solutionContainerSystem.TryGetSolution((ent, ent), solutionName, out soln, out solution);
                solutionEntityUid = ent;
            }

            if (solutionEntityUid is null
                || soln is null
                || solution is null
                || solution.Contents.Count == 0)
            {
                return;
            }

            // randomize the reagent list so we don't have any weird quirks
            // like alphabetical order or insertion order mattering for processing
            var list = solution.Contents.ToArray();
            _random.Shuffle(list);

            int reagents = 0;
            foreach (var (reagent, quantity) in list)
            {
                if (!_prototypeManager.TryIndex<ReagentPrototype>(reagent.Prototype, out var proto))
                    continue;

                var mostToRemove = FixedPoint2.Zero;

                // Funkystation - Genetics
                // Chemical resistance mutation - completely ignore selected chem metabolism
                var bodyUid = ent.Comp2?.Body ?? solutionEntityUid.Value;

                if (TryComp<ChemicalResistanceComponent>(bodyUid, out var resistance) && resistance.Reagents.Contains(reagent.Prototype))
                {
                    var removeAmount = FixedPoint2.Min(resistance.PurgeAmount, quantity);
                    solution.RemoveReagent(reagent, removeAmount);
                    reagents += 1;
                    continue; // Skip normal metabolism and effects
                }
                // Funkystation - end of genetics changes

                if (proto.Metabolisms is null)
                {
                    if (ent.Comp1.RemoveEmpty)
                    {
                        solution.RemoveReagent(reagent, FixedPoint2.New(1));
                    }

                    continue;
                }

                // Frontier: all cryogenic reagents in the solution should be processed, others should be limited (buff cryo meds)
                if (reagents >= ent.Comp1.MaxReagentsProcessable && !proto.Metabolisms.ContainsKey("Cryogenic"))
                    continue;
                // End Frontier


                // loop over all our groups and see which ones apply
                if (ent.Comp1.MetabolismGroups is null)
                    continue;

                foreach (var group in ent.Comp1.MetabolismGroups)
                {
                    if (!proto.Metabolisms.TryGetValue(group.Id, out var entry))
                        continue;

                    var rate = entry.MetabolismRate * (float) group.MetabolismRateModifier;
                    if (rate <= FixedPoint2.Zero)
                        continue;

                    var rateFloat = (float) rate;
                    if (!float.IsFinite(rateFloat) || rateFloat <= 0f)
                        continue;

                    mostToRemove = FixedPoint2.Min(rate, quantity);
                    if (mostToRemove <= FixedPoint2.Zero)
                        continue;

                    // Frontier: skip applying effects in metabolism
                    if (group.SkipEffects)
                        continue;
                    // End Frontier

                    float scale = (float) mostToRemove / rateFloat;
                    if (!float.IsFinite(scale) || scale <= 0f)
                        continue;

                    // HardLight start: Check if the entity is a Synth and scale effects accordingly when dead
                    var actualEntity = ent.Comp2?.Body ?? solutionEntityUid.Value;
                    var isSynth = HasComp<HLSynthComponent>(actualEntity);

                    // if it's possible for them to be dead, and they are,
                    // then we shouldn't process any effects (unless they're a synth),
                    // but should probably still remove reagents
                    var isDead = false;
                    if (TryComp<MobStateComponent>(actualEntity, out var state))
                    {
                        isDead = _mobStateSystem.IsDead(actualEntity, state);
                        if (!proto.WorksOnTheDead && !isSynth && isDead)
                            continue;
                    }

                    if (isSynth && isDead && !proto.WorksOnTheDead)
                        scale *= 0.75f;

                    if (!float.IsFinite(scale) || scale <= 0f)
                        continue;
                    // HardLight end

                    var args = new EntityEffectReagentArgs(actualEntity, EntityManager, ent, solution, mostToRemove, proto, null, scale);

                    // do all effects, if conditions apply
                    foreach (var effect in entry.Effects)
                    {
                        if (!effect.ShouldApply(args, _random))
                            continue;

                        if (effect.ShouldLog)
                        {
                            _adminLogger.Add(
                                LogType.ReagentEffect,
                                effect.LogImpact,
                                $"Metabolism effect {effect.GetType().Name:effect}"
                                + $" of reagent {proto.LocalizedName:reagent}"
                                + $" applied on entity {actualEntity:entity}"
                                + $" at {Transform(actualEntity).Coordinates:coordinates}"
                            );
                        }

                        effect.Effect(args);
                    }
                }

                // remove a certain amount of reagent
                if (mostToRemove > FixedPoint2.Zero)
                {
                    solution.RemoveReagent(reagent, mostToRemove);
                    // Frontier: do not count cryogenics chems against the reagent limit (to buff cryo meds)
                    if (!proto.Metabolisms.ContainsKey("Cryogenic"))
                        reagents++;
                    // End Frontier
                }
            }

            _solutionContainerSystem.UpdateChemicals(soln.Value);
        }
    }

    // TODO REFACTOR THIS
    // This will cause rates to slowly drift over time due to floating point errors.
    // Instead, the system that raised this should trigger an update and subscribe to get-modifier events.
    [ByRefEvent]
    public readonly record struct ApplyMetabolicMultiplierEvent(
        EntityUid Uid,
        float Multiplier,
        bool Apply)
    {
        /// <summary>
        /// The entity whose metabolism is being modified.
        /// </summary>
        public readonly EntityUid Uid = Uid;

        /// <summary>
        /// What the metabolism's update rate will be multiplied by.
        /// </summary>
        public readonly float Multiplier = Multiplier;

        /// <summary>
        /// If true, apply the multiplier. If false, revert it.
        /// </summary>
        public readonly bool Apply = Apply;
    }
}
