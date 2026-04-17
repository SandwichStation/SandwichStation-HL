using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared.EntityEffects;
using Content.Shared.Localizations;
using Robust.Shared.Prototypes;

namespace Content.Server.EntityEffects.Effects;

/// <summary>
/// Restores the entity's blood to its original reagent type.
/// Used by the Abyssal Cleansing Serum to restore corrupted blood to the patient's original blood type.
/// </summary>
public sealed partial class RestoreBloodReagent : EntityEffect
{
    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-restore-blood-reagent");

    public override void Effect(EntityEffectBaseArgs args)
    {
        if (args.EntityManager.TryGetComponent<BloodstreamComponent>(args.TargetEntity, out var bloodstream))
        {
            // Only restore if there's an original blood type stored and it's different from current
            if (bloodstream.OriginalBloodReagent != null && 
                bloodstream.BloodReagent != bloodstream.OriginalBloodReagent)
            {
                var sys = args.EntityManager.System<BloodstreamSystem>();
                sys.ChangeBloodReagent(args.TargetEntity, bloodstream.OriginalBloodReagent.Value, bloodstream);
                
                // Use system method to clear the original blood reagent
                sys.ClearOriginalBloodReagent(args.TargetEntity, bloodstream);
                
                // Remove any blood modification tracker since we're actively purifying
                if (args.EntityManager.TryGetComponent<BloodModificationTrackerComponent>(args.TargetEntity, out var tracker))
                {
                    args.EntityManager.RemoveComponent<BloodModificationTrackerComponent>(args.TargetEntity);
                }
            }
        }
    }
}