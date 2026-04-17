using Content.Shared.Drugs;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Player;

namespace Content.Client.Drugs;

/// <summary>
///     System to handle drug related overlays.
/// </summary>
public sealed class DrugOverlaySystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IOverlayManager _overlayMan = default!;

    private RainbowOverlay _rainbowOverlay = default!;
    private AbyssalOverlay _abyssalOverlay = default!;

    public static string RainbowKey = "SeeingRainbows";
    public static string AbyssalKey = "AbyssalWhispers";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SeeingRainbowsComponent, ComponentInit>(OnRainbowInit);
        SubscribeLocalEvent<SeeingRainbowsComponent, ComponentShutdown>(OnRainbowShutdown);
        SubscribeLocalEvent<SeeingRainbowsComponent, LocalPlayerAttachedEvent>(OnRainbowPlayerAttached);
        SubscribeLocalEvent<SeeingRainbowsComponent, LocalPlayerDetachedEvent>(OnRainbowPlayerDetached);

        SubscribeLocalEvent<AbyssalWhispersComponent, ComponentInit>(OnAbyssalInit);
        SubscribeLocalEvent<AbyssalWhispersComponent, ComponentShutdown>(OnAbyssalShutdown);
        SubscribeLocalEvent<AbyssalWhispersComponent, LocalPlayerAttachedEvent>(OnAbyssalPlayerAttached);
        SubscribeLocalEvent<AbyssalWhispersComponent, LocalPlayerDetachedEvent>(OnAbyssalPlayerDetached);

        _rainbowOverlay = new();
        _abyssalOverlay = new();
    }

    // Rainbow overlay events
    private void OnRainbowPlayerAttached(EntityUid uid, SeeingRainbowsComponent component, LocalPlayerAttachedEvent args)
    {
        _overlayMan.AddOverlay(_rainbowOverlay);
    }

    private void OnRainbowPlayerDetached(EntityUid uid, SeeingRainbowsComponent component, LocalPlayerDetachedEvent args)
    {
        _rainbowOverlay.Intoxication = 0;
        _rainbowOverlay.TimeTicker = 0;
        _overlayMan.RemoveOverlay(_rainbowOverlay);
    }

    private void OnRainbowInit(EntityUid uid, SeeingRainbowsComponent component, ComponentInit args)
    {
        if (_player.LocalEntity == uid)
            _overlayMan.AddOverlay(_rainbowOverlay);
    }

    private void OnRainbowShutdown(EntityUid uid, SeeingRainbowsComponent component, ComponentShutdown args)
    {
        if (_player.LocalEntity == uid)
        {
            _rainbowOverlay.Intoxication = 0;
            _rainbowOverlay.TimeTicker = 0;
            _overlayMan.RemoveOverlay(_rainbowOverlay);
        }
    }

    // Abyssal overlay events
    private void OnAbyssalPlayerAttached(EntityUid uid, AbyssalWhispersComponent component, LocalPlayerAttachedEvent args)
    {
        _overlayMan.AddOverlay(_abyssalOverlay);
    }

    private void OnAbyssalPlayerDetached(EntityUid uid, AbyssalWhispersComponent component, LocalPlayerDetachedEvent args)
    {
        _abyssalOverlay.Intoxication = 0;
        _abyssalOverlay.TimeTicker = 0;
        _overlayMan.RemoveOverlay(_abyssalOverlay);
    }

    private void OnAbyssalInit(EntityUid uid, AbyssalWhispersComponent component, ComponentInit args)
    {
        if (_player.LocalEntity == uid)
            _overlayMan.AddOverlay(_abyssalOverlay);
    }

    private void OnAbyssalShutdown(EntityUid uid, AbyssalWhispersComponent component, ComponentShutdown args)
    {
        if (_player.LocalEntity == uid)
        {
            _abyssalOverlay.Intoxication = 0;
            _abyssalOverlay.TimeTicker = 0;
            _overlayMan.RemoveOverlay(_abyssalOverlay);
        }
    }
}
