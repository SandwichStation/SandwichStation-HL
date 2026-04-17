using Content.Server.DoAfter;
using Content.Server.EUI;
using Content.Server.Popups;
using Content.Server.Stunnable;
using Content.Shared._HL.Brainwashing;
using Content.Shared.Clothing;
using Content.Shared.Coordinates;
using Content.Shared.DoAfter;
using Content.Shared.Flash;
using Content.Shared.Flash.Components;
using Content.Shared.Mindshield.Components;
using Content.Shared.StatusEffect;
using Content.Shared.Verbs;
using Robust.Server.Audio;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Utility;

namespace Content.Server._HL.Brainwashing;

public sealed class BrainwashVizorSystem : SharedBrainwashVizorSystem
{
    [Dependency] private readonly SharedBrainwashedSystem _sharedBrainwashedSystem = default!;
    [Dependency] private readonly DoAfterSystem _doAfterSystem = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly EuiManager _euiManager = default!;
    [Dependency] private readonly AudioSystem _audioSystem = default!;
    [Dependency] private readonly StatusEffectsSystem _statusEffectsSystem = default!;
    [Dependency] private readonly SharedFlashSystem _flashSystem = default!;
    [Dependency] private readonly MovementModStatusSystem _movementMod = default!;
    [Dependency] private readonly PopupSystem _popupSystem = default!;
    public override void Initialize()
    {
        SubscribeLocalEvent<BrainwashVizorComponent, GetVerbsEvent<Verb>>(ConfigureVerb);
        SubscribeLocalEvent<BrainwashVizorComponent, ClothingGotEquippedEvent>(OnEquipped);
        SubscribeLocalEvent<BrainwashVizorComponent, ClothingGotUnequippedEvent>(OnUnequipped);
        SubscribeLocalEvent<BrainwashVizorComponent, EngagedEvent>(Engaged);
    }

    private void OnUnequipped(EntityUid uid, BrainwashVizorComponent component, ClothingGotUnequippedEvent args)
    {
        if (component.DoAfter == null)
            return;

        _doAfterSystem.Cancel(component.DoAfter);
        component.DoAfter = null;
    }

    private void Engaged(EntityUid uid, BrainwashVizorComponent component, EngagedEvent args)
    {
        component.DoAfter = null; // Informs the component the doAfter doesn't exist anymore

        if (args.Cancelled) // Now we can let the handlers do our doafter work again yippie :DD
            return;

        var user = _entityManager.GetEntity(args.Wearer);
        TryComp<BrainwashedComponent>(uid, out var brainwashedComponent);
        TryGetNetEntity(user, out var userNetEntity);
        if (userNetEntity == null || brainwashedComponent == null)
            return;

        var userIsMindshielded = HasComp<MindShieldComponent>(user);
        if (userIsMindshielded)
        {
            _popupSystem.PopupCoordinates("Installation failed!", user.ToCoordinates());
            return;
        }

        _audioSystem.PlayPvs(brainwashedComponent.EngageSound, uid, new AudioParams());
        _statusEffectsSystem.TryAddStatusEffect<FlashedComponent>(user,
            _flashSystem.FlashedKey,
            TimeSpan.FromSeconds(5),
            true);
        _stun.TrySlowdown(user, TimeSpan.FromSeconds(5), true, 0, 0);
        TryComp<BrainwashedComponent>(user, out var newBrainwashedComponent);
        if (newBrainwashedComponent == null)
            AddComp<BrainwashedComponent>(user);
        _sharedBrainwashedSystem.SetCompulsions(user, brainwashedComponent.Compulsions);
        var brainwashedEvent = new BrainwashedEvent();
        RaiseLocalEvent(user, brainwashedEvent);
        RaiseNetworkEvent(brainwashedEvent, user);
    }

    private void OnEquipped(EntityUid uid, BrainwashVizorComponent component, ClothingGotEquippedEvent args)
    {
        TryComp<BrainwashedComponent>(uid, out var brainwashedComponent);
        TryGetNetEntity(args.Wearer, out var netEntity);
        TryComp<DoAfterComponent>(args.Wearer, out var doAfterComponent);
        if (netEntity == null || component.DoAfter != null || doAfterComponent == null || brainwashedComponent == null)
            return;

        var doAfterArgs = new DoAfterArgs(_entityManager,
            args.Wearer,
            TimeSpan.FromSeconds(3),
            new EngagedEvent(netEntity.Value),
            uid)
        {
            RequireCanInteract = false,
        };
        var startDoAfterSuccess = _doAfterSystem.TryStartDoAfter(doAfterArgs, out var doAfterId, doAfterComponent);
        if (!startDoAfterSuccess)
            return;
        component.DoAfter = doAfterId;
        _audioSystem.PlayPvs(brainwashedComponent.ChargingSound, uid, new AudioParams());
    }

    private void ConfigureVerb(EntityUid uid, BrainwashVizorComponent component, GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;
        args.Verbs.Add(new Verb
        {
            Act = () =>
            {
                var ui = new BrainwashEditor(_sharedBrainwashedSystem);
                TryComp<BrainwashedComponent>(uid, out var brainwashedComponent);
                if (brainwashedComponent == null)
                    return;
                if (!_playerManager.TryGetSessionByEntity(args.User, out var session))
                    return;
                _euiManager.OpenEui(ui, session);
                ui.UpdateCompulsions(brainwashedComponent, uid);
            },
            Text = "Configure Neuralyzer",
            Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/sentient.svg.192dpi.png")),
            Priority = 1
        });
    }
}
