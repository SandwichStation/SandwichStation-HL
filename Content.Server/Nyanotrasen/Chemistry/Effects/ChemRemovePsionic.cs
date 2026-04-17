using Content.Shared.Chemistry.Reagent;
using Content.Server.Abilities.Psionics;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;
using Content.Shared.EntityEffects;

namespace Content.Server.Chemistry.ReagentEffects
{
    /// <summary>
    /// Rerolls psionics once.
    /// </summary>
    [UsedImplicitly]
    public sealed partial class ChemRemovePsionic : EntityEffect
    {
        protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
            => Loc.GetString("reagent-effect-guidebook-chem-remove-psionic", ("chance", Probability));

        public override void Effect(EntityEffectBaseArgs args)
        {
            if (args is EntityEffectReagentArgs reagentArgs && reagentArgs.Scale.Float() != 1f)
                return;

            var psySys = args.EntityManager.EntitySysManager.GetEntitySystem<PsionicAbilitiesSystem>();

            psySys.RemoveAllPsionicPowers(args.TargetEntity);
        }
    }
}
