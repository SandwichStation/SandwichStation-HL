using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared._HL.Damage;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._HL.Damage;

public sealed class HLBloodLevelPassiveDamageSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly BloodstreamSystem _bloodstream = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HLBloodLevelPassiveDamageComponent, MapInitEvent>(OnPendingMapInit);
    }

    private void OnPendingMapInit(Entity<HLBloodLevelPassiveDamageComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextDamage = _timing.CurTime + ent.Comp.Interval;
        Dirty(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;

        // Go through every entity with the component
        var query = EntityQueryEnumerator<HLBloodLevelPassiveDamageComponent, DamageableComponent, MobStateComponent, BloodstreamComponent>();
        while (query.MoveNext(out var uid, out var comp, out var damage, out var mobState, out var bloodstream))
        {
            // Make sure they're up for a damage tick
            if (comp.NextDamage > curTime)
                continue;

            if (comp.MaximumDamage != 0 && damage.TotalDamage >= comp.MaximumDamage)
                continue;

            var hasDamage = false;
            foreach (var allowedGroup in comp.AllowedDamageGroups)
            {
                if (HasDamage((uid, damage), allowedGroup, comp.MinimumDamage))
                {
                    hasDamage = true;
                    break;
                }
            }

            if (!hasDamage)
                continue;

            var bloodLevelPercentage = _bloodstream.GetBloodLevelPercentage(uid, bloodstream);

            // Can't heal with no blood
            if (bloodLevelPercentage <= 0)
                continue;

            // Set the next time they can take damage
            comp.NextDamage = curTime + comp.Interval;

            // Proceed
            foreach (var allowedState in comp.AllowedStates)
            {
                if (allowedState != mobState.CurrentState)
                    continue;

                // For example, 20% blood level would muliply damage by the BloodLevelDamageMultiplier to the power of 8
                var power = (1 - bloodLevelPercentage) / 1 * 10;
                var multiplier = MathF.Pow(comp.BloodLevelDamageMultiplier, power);
                var damageToGive = comp.Damage * multiplier;

                if (bloodstream.BleedAmount < comp.MinimumBleedAmount && bloodLevelPercentage > comp.MaximumBloodPercentage)
                    _bloodstream.TryModifyBloodLevel(uid, -comp.BloodLevelDecrease, bloodstream);

                _damageable.TryChangeDamage(uid, damageToGive, true, false, damage);

                break;
            }
        }
    }

    public bool HasDamage(Entity<DamageableComponent> ent, ProtoId<DamageGroupPrototype> group, FixedPoint2 minimumDamage)
    {
        if (ent.Comp.Damage.Empty)
            return false;

        var damage = ent.Comp.Damage.GetDamagePerGroup(_prototypes);
        var groupDmg = damage.GetValueOrDefault(group);

        if (groupDmg <= minimumDamage)
            return false;

        return true;
    }
}
