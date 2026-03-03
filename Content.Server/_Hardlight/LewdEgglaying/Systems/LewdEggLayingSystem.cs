using Content.Shared.Actions;
using Content.Shared.Actions.Events;
using Content.Shared.DoAfter;
using Content.Shared.IdentityManagement;
using Content.Shared.Movement.Systems;
using Content.Shared.Storage;
using Content.Shared.Traits.Events;
using Content.Shared.Verbs;
using Content.Server.Animals.Components;
using Content.Server.Popups;
using Robust.Server.Audio;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.Animals.Systems;

/// <summary>
///     Gives the ability to lay eggs/other things;
///     produces endlessly if the owner does not have a HungerComponent.
/// </summary>
public sealed class LewdEggLayingSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeedModifier = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LewdEggLayingComponent, ComponentShutdown>(OnHostShutdown);
        SubscribeLocalEvent<LewdEggLayingComponent, GetVerbsEvent<InnateVerb>>(AddLayEggInsideVerb);
        SubscribeLocalEvent<LewdEggLayingComponent, LewdEggLayingActionEvent>(OnEggLayingAction);
        SubscribeLocalEvent<LewdEggLayingComponent, LewdEggLayingDoAfterEvent>(OnEggLayingDoAfter);
        SubscribeLocalEvent<LewdEggLayingComponent, LewdEggLayingInsideDoAfterEvent>(OnEggLayingInsideDoAfter);
        SubscribeLocalEvent<LewdEggLayingComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovespeed);
    }

    private void OnHostShutdown(EntityUid user, LewdEggLayingComponent eggLaying, ComponentShutdown args)
    {
        if (TerminatingOrDeleted(user))
            return;

        _actions.RemoveAction(user, eggLaying.Action);
    }

    private void AddLayEggInsideVerb(Entity<LewdEggLayingComponent> user, ref GetVerbsEvent<InnateVerb> args)
    {
        // Todo figure out how to only make verb appear for player mobs
        var target = args.Target;
        if (!args.CanInteract || user.Owner == target || !user.Comp.hasEggs() || !TryComp(target, out ActorComponent? actor))
            return;

        InnateVerb verbLayEgg = new()
        {
            Act = () => AttemptLayInside(user, target),
            Text = Loc.GetString($"lay-egg-inside-verb-get-text"),
            Priority = 1
        };
        args.Verbs.Add(verbLayEgg);
    }

    private void AttemptLayInside(Entity<LewdEggLayingComponent> user, EntityUid target)
    {
        var doargs = new DoAfterArgs(EntityManager, user.Owner, user.Comp.EggLayDelay, new LewdEggLayingInsideDoAfterEvent(), user.Owner, target)
        {
            BreakOnMove = true,
            BlockDuplicate = true,
            BreakOnDamage = true,
            CancelDuplicate = true,
        };

        _popup.PopupEntity(Loc.GetString("action-popup-lay-egg-inside-start", ("entity", Identity.Entity(user.Owner, EntityManager)), ("target", Identity.Entity(target, EntityManager))), user);
        _doAfter.TryStartDoAfter(doargs);
    }

    private void OnRefreshMovespeed(EntityUid user, LewdEggLayingComponent eggLaying, RefreshMovementSpeedModifiersEvent args)
    {
        if (eggLaying.isHeavyOfEggs())
        {
            args.ModifySpeed(eggLaying.EggSlowMult, eggLaying.EggSlowMult);
        }
    }

    private void OnEggLayingAction(EntityUid user, LewdEggLayingComponent eggLaying, LewdEggLayingActionEvent args)
    {
        if (!eggLaying.hasEggs())
        {
            _popup.PopupEntity(Loc.GetString("action-popup-lay-egg-no-eggs"), user, user);
            return;
        }

        var doAfter = new DoAfterArgs(EntityManager, user, eggLaying.EggLayDelay, new LewdEggLayingDoAfterEvent(), user)
        {
            BreakOnMove = true,
            BlockDuplicate = true,
            BreakOnDamage = true,
            CancelDuplicate = true,
        };

        _popup.PopupEntity(Loc.GetString("action-popup-lay-egg-user-start"), user, user);
        _doAfter.TryStartDoAfter(doAfter);
    }

    public void Inseminate(EntityUid user, float amount, LewdEggLayingComponent? eggLaying = null)
    {
        if (!Resolve(user, ref eggLaying) || eggLaying.Temporary)
            return;

        amount *= eggLaying.ProductionMult;

        bool hasEggsBefore = eggLaying.hasEggs();
        bool isHeavyBefore = eggLaying.isHeavyOfEggs();
        bool isFullBefore = eggLaying.isFullOfEggs();

        eggLaying.addEggs(amount);

        if(eggLaying.hasEggs() && !hasEggsBefore)
        {
            _popup.PopupEntity(Loc.GetString("action-popup-lay-egg-firstegg"), user, user);
            if (!TerminatingOrDeleted(user))
                _actions.AddAction(user, ref eggLaying.Action, eggLaying.ActionPrototype);
        }
        else if(eggLaying.isHeavyOfEggs() && !isHeavyBefore)
        {
            _movementSpeedModifier.RefreshMovementSpeedModifiers(user);
            _popup.PopupEntity(Loc.GetString("action-popup-lay-egg-heavyeggs"), user, user);
        }
        else if(eggLaying.isFullOfEggs() && !isFullBefore)
        {
            _popup.PopupEntity(Loc.GetString("action-popup-lay-egg-fulleggs"), user, user);
        }
        else if(eggLaying.doFlavor())
        {
            _popup.PopupEntity(Loc.GetString(_random.Pick(eggLaying.FlavorMessages)), user, user);
        }
    }

    private void OnEggLayingInsideDoAfter(EntityUid user, LewdEggLayingComponent myEggs, LewdEggLayingInsideDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target == null)
            return;
            
        args.Handled = true;

        if (myEggs.Deleted || !myEggs.hasEggs())
        {
            _popup.PopupEntity(Loc.GetString("action-popup-lay-egg-noeggs"), user, user);
            return;
        }
        var target = args.Target.Value;

        _audio.PlayPvs(myEggs.EggLaySound, user);

        if (!TryComp<LewdEggLayingComponent>(target, out var theirEggs))
        {
            theirEggs = (LewdEggLayingComponent)Factory.GetComponent(Factory.GetComponentName<LewdEggLayingComponent>());
            EntityManager.AddComponent(target, theirEggs);
            theirEggs.makeTempFrom(myEggs);
            if (!TerminatingOrDeleted(target))
                _actions.AddAction(target, ref theirEggs.Action, theirEggs.ActionPrototype);
        }

        myEggs.addEggs(-1.0f);
        theirEggs.addEggs(1.0f);
        _movementSpeedModifier.RefreshMovementSpeedModifiers(user);
        _movementSpeedModifier.RefreshMovementSpeedModifiers(target);

        if(myEggs.hasEggs())
        {
            _popup.PopupEntity(Loc.GetString("action-popup-lay-egg-inside-give-more", ("entity", Identity.Entity(target, EntityManager))), user, user);
            _popup.PopupEntity(Loc.GetString("action-popup-lay-egg-inside-receive-more", ("entity", Identity.Entity(user, EntityManager))), target, target);
            args.Repeat = true;
        }
        else
        {
            _popup.PopupEntity(Loc.GetString("action-popup-lay-egg-inside-give-done", ("entity", Identity.Entity(target, EntityManager))), user, user);
            _popup.PopupEntity(Loc.GetString("action-popup-lay-egg-inside-receive-done", ("entity", Identity.Entity(user, EntityManager))), target, target);

            if(myEggs.Temporary)
                EntityManager.RemoveComponent<LewdEggLayingComponent>(user);
            else
                _actions.RemoveAction(user, myEggs.Action);
        }
    }

    private void OnEggLayingDoAfter(EntityUid user, LewdEggLayingComponent eggLaying, LewdEggLayingDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;
            
        args.Handled = true;

        if (eggLaying.Deleted || !eggLaying.hasEggs())
        {
            _popup.PopupEntity(Loc.GetString("action-popup-lay-egg-noeggs"), user, user);
            return;
        }

        foreach (var ent in EntitySpawnCollection.GetSpawns(eggLaying.EggSpawn, _random))
        {
            Spawn(ent, Transform(user).Coordinates);
        }

        _audio.PlayPvs(eggLaying.EggLaySound, user);

        eggLaying.addEggs(-1.0f);
        _movementSpeedModifier.RefreshMovementSpeedModifiers(user);

        if(eggLaying.hasEggs())
        {
            _popup.PopupEntity(Loc.GetString("action-popup-lay-egg-user-more"), user, user);
            args.Repeat = true;
        }
        else
        {
            _popup.PopupEntity(Loc.GetString("action-popup-lay-egg-user-done"), user, user);

            if(eggLaying.Temporary)
                EntityManager.RemoveComponent<LewdEggLayingComponent>(user);
            else
                _actions.RemoveAction(user, eggLaying.Action);
        }
        _popup.PopupEntity(Loc.GetString("action-popup-lay-egg-others", ("entity", user)), user, Filter.PvsExcept(user), true);
    }
}
