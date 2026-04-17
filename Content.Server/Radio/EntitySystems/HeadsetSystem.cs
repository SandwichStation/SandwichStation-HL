using Content.Server.Chat.Systems;
using Content.Server.Emp;
using Content.Server.Radio.Components;
using Content.Shared.Inventory.Events;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Content.Shared.Radio.EntitySystems;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server.Radio.EntitySystems;

public sealed class HeadsetSystem : SharedHeadsetSystem
{
    [Dependency] private readonly INetManager _netMan = default!;
    [Dependency] private readonly RadioSystem _radio = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<HeadsetComponent, RadioReceiveEvent>(OnHeadsetReceive);

        SubscribeLocalEvent<WearingHeadsetComponent, EntitySpokeEvent>(OnSpeak);

        SubscribeLocalEvent<HeadsetComponent, EmpPulseEvent>(OnEmpPulse);
    }

    private void OnSpeak(EntityUid uid, WearingHeadsetComponent component, EntitySpokeEvent args)
    {
        // HardLight start: Use the headset's active radio channels (includes intrinsic + keys).
        if (args.Channel == null)
            return;

        if (TryComp(component.Headset, out ActiveRadioComponent? activeRadio)
            && activeRadio.Channels.Contains(args.Channel.ID))
        // HardLight end
        {
            _radio.SendRadioMessage(uid, args.Message, args.Channel, component.Headset);
            args.Channel = null; // prevent duplicate messages from other listeners.
        }
    }

    protected override void OnGotEquipped(EntityUid uid, HeadsetComponent component, GotEquippedEvent args)
    {
        base.OnGotEquipped(uid, component, args);
        component.IsEquipped = true; // HardLight
        if (component.Enabled) // HardLight
        {
            EnsureComp<WearingHeadsetComponent>(args.Equipee).Headset = uid;
            // HardLight start: Trigger channel update via EncryptionChannelsChangedEvent (handled by IntrinsicRadioKeySystem)
            if (TryComp<EncryptionKeyHolderComponent>(uid, out var keyHolder))
            {
                RaiseLocalEvent(uid, new EncryptionChannelsChangedEvent(keyHolder));
            }
            // HardLight end
        }
    }

    protected override void OnGotUnequipped(EntityUid uid, HeadsetComponent component, GotUnequippedEvent args)
    {
        base.OnGotUnequipped(uid, component, args);
        component.IsEquipped = false;
        // HardLight start: Clear working channels but preserve intrinsic channels for when it's re-equipped
        if (TryComp<ActiveRadioComponent>(uid, out var activeRadio))
        {
            activeRadio.Channels.Clear();
            activeRadio.Channels.UnionWith(activeRadio.IntrinsicChannels);
        }
        // HardLight end

        RemComp<WearingHeadsetComponent>(args.Equipee);
    }

    public void SetEnabled(EntityUid uid, bool value, HeadsetComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        if (component.Enabled == value)
            return;

        if (!value)
        {
            RemCompDeferred<ActiveRadioComponent>(uid);

            if (component.IsEquipped)
                RemCompDeferred<WearingHeadsetComponent>(Transform(uid).ParentUid);
        }
        else if (component.IsEquipped)
        {
            EnsureComp<WearingHeadsetComponent>(Transform(uid).ParentUid).Headset = uid;
            // HardLight start: Trigger channel update via EncryptionChannelsChangedEvent (handled by IntrinsicRadioKeySystem)
            if (TryComp<EncryptionKeyHolderComponent>(uid, out var keyHolder))
            {
                RaiseLocalEvent(uid, new EncryptionChannelsChangedEvent(keyHolder));
            }
            // HardLight end
        }
    }

    private void OnHeadsetReceive(EntityUid uid, HeadsetComponent component, ref RadioReceiveEvent args)
    {
        if (TryComp(Transform(uid).ParentUid, out ActorComponent? actor))
            _netMan.ServerSendMessage(args.ChatMsg, actor.PlayerSession.Channel);
    }

    private void OnEmpPulse(EntityUid uid, HeadsetComponent component, ref EmpPulseEvent args)
    {
        if (component.Enabled)
        {
            args.Affected = true;
            args.Disabled = true;
        }
    }
}
