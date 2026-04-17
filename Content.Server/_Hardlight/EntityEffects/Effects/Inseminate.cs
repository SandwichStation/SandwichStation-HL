using Content.Shared.Chemistry.Reagent;
using Content.Shared.EntityEffects;
using Content.Server.Animals.Components;
using Content.Server.Animals.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server.EntityEffects.Effects
{
    /// <summary>
    /// Attempts to find an egglaying or pregnancy component and triggers their effects
    /// </summary>
    public sealed partial class Inseminate : EntityEffect
    {
        public override void Effect(EntityEffectBaseArgs args)
        {
            var entman = args.EntityManager;
            if (entman.TryGetComponent(args.TargetEntity, out LewdEggLayingComponent? egglaying))
            {
                float amt = (args is EntityEffectReagentArgs reagentArgs) ? (float) reagentArgs.Quantity : 1.0f;
                entman.System<LewdEggLayingSystem>().Inseminate(args.TargetEntity, amt, egglaying);
            }
        }

        protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
            => Loc.GetString("reagent-effect-guidebook-inseminate", ("chance", Probability));
    }
}
