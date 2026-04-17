using Content.Shared.Clothing.Components;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Content.Shared.Whitelist;
using Robust.Shared.Serialization;

namespace Content.Shared.InteractionVerbs.Requirements;

/// <summary>
///     Requires the target to meet a certain whitelist and not meet a blacklist.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class EntityWhitelistRequirement : InteractionRequirement
{
    [DataField] public EntityWhitelist? Whitelist = new(), Blacklist = new();

    [NonSerialized] private EntityWhitelistSystem? _wlField; // Floofstation - wizden changed whitelists so we have to retrofit this

    public override bool IsMet(InteractionArgs args, InteractionVerbPrototype proto, InteractionAction.VerbDependencies deps)
    {
        // Floofstation - changed to conform to the new style (bleugh)
        _wlField ??= IoCManager.Resolve<IEntitySystemManager>().GetEntitySystem<EntityWhitelistSystem>();
        return _wlField.CheckBoth(args.Target, Blacklist, Whitelist);
    }
}

/// <summary>
///     Requires the mob to be a mob in a certain state. If inverted, requires the mob to not be in that state.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class MobStateRequirement : InvertableInteractionRequirement
{
    [DataField] public List<MobState> AllowedStates = new();

    public override bool IsMet(InteractionArgs args, InteractionVerbPrototype proto, InteractionAction.VerbDependencies deps)
    {
        if (!deps.EntMan.TryGetComponent<MobStateComponent>(args.Target, out var state))
            return false;

        return AllowedStates.Contains(state.CurrentState) ^ Inverted;
    }
}

/// <summary>
///     Requires the target to be in a specific standing state.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class StandingStateRequirement : InteractionRequirement
{
    [DataField] public bool AllowStanding, AllowLaying, AllowKnockedDown;

    public override bool IsMet(InteractionArgs args, InteractionVerbPrototype proto, InteractionAction.VerbDependencies deps)
    {
        if (deps.EntMan.HasComponent<KnockedDownComponent>(args.Target))
            return AllowKnockedDown;

        if (!deps.EntMan.TryGetComponent<StandingStateComponent>(args.Target, out var state))
            return false;

        return state.CurrentState == StandingState.Standing && AllowStanding
            || state.CurrentState == StandingState.Lying && AllowLaying;
    }
}

/// <summary>
///     Requires the target to be the user itself.
/// </summary>
[Serializable, NetSerializable]
// Hardlight - Begin
public sealed partial class SlotObstructionRequirement : InvertableInteractionRequirement
{
    [DataField] public string Slot = "mask";
    [DataField] public bool CheckUser = false;

    public override bool IsMet(InteractionArgs args, InteractionVerbPrototype proto, InteractionAction.VerbDependencies deps)
    {
        if (string.IsNullOrWhiteSpace(Slot))
        {
            return false;
        }

        var entityToCheck = CheckUser ? args.User : args.Target;
        var inv = IoCManager.Resolve<IEntitySystemManager>().GetEntitySystem<InventorySystem>();

        if (!inv.TryGetSlotEntity(entityToCheck, Slot, out var slotEntity))
        {
            // Slot is empty, so no obstruction.
            return true ^ Inverted;
        }

        // Check if it's a mask and if it's toggled (pulled down).
        if (deps.EntMan.TryGetComponent<MaskComponent>(slotEntity, out var maskComp))
        {
            // If toggled (pulled down), not obstructing (true).
            // If not toggled (pulled up), obstructing (false).
            var result = maskComp.IsToggled ^ Inverted;
            if (!result)
            {
                args.Blackboard["interaction-verb-failure-locale"] = "interaction-verb-mask-blocked";
            }
            return result;
        }

        // If it's not a mask, assume it's obstructing.
        args.Blackboard["interaction-verb-failure-locale"] = "interaction-verb-mask-blocked";
        return false ^ Inverted;
    }
}
// Hardlight - End

public sealed partial class SelfTargetRequirement : InvertableInteractionRequirement
{
    public override bool IsMet(InteractionArgs args, InteractionVerbPrototype proto, InteractionAction.VerbDependencies deps)
    {
        return (args.Target == args.User) ^ Inverted;
    }
}
