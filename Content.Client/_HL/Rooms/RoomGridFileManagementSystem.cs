using Content.Shared._HL.Rooms;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using System;

namespace Content.Client._HL.Rooms;

public sealed class RoomGridFileManagementSystem : EntitySystem
{
    [Dependency] private readonly IResourceManager _resourceManager = default!;

    private const string ExportsRoot = "/Exports";
    private const string RoomPrefix = "room_";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<RequestRoomGridLoadMessage>(OnRequestRoomGridLoad);
        SubscribeNetworkEvent<SendRoomGridSaveDataClientMessage>(OnSaveRoomData);
    }

    private void OnRequestRoomGridLoad(RequestRoomGridLoadMessage message)
    {
        var safeKey = SanitizeKey(message.CharacterKey);
        var filePath = $"{ExportsRoot}/{RoomPrefix}{safeKey}.yml";
        string yamlData = string.Empty;
        var found = false;

        try
        {
            if (_resourceManager.UserData.Exists(new(filePath)))
            {
                using var reader = _resourceManager.UserData.OpenText(new(filePath));
                yamlData = reader.ReadToEnd();
                found = !string.IsNullOrWhiteSpace(yamlData);
            }
        }
        catch (Exception ex)
        {
            Logger.GetSawmill("hardlight").Error($"Failed to load room data from {filePath}: {ex.Message}");
        }

        RaiseNetworkEvent(new SendRoomGridDataMessage(message.ConsoleNetEntity, message.CharacterKey, yamlData, found));
    }

    private void OnSaveRoomData(SendRoomGridSaveDataClientMessage message)
    {
        var safeKey = SanitizeKey(message.CharacterKey);
        var filePath = $"{ExportsRoot}/{RoomPrefix}{safeKey}.yml";

        try
        {
            _resourceManager.UserData.CreateDir(new(ExportsRoot));
            using var writer = _resourceManager.UserData.OpenWriteText(new(filePath));
            writer.Write(message.RoomData);
        }
        catch (Exception ex)
        {
            Logger.GetSawmill("hardlight").Error($"Failed to save room data to {filePath}: {ex.Message}");
        }
    }

    private static string SanitizeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return "unknown";

        var chars = new char[key.Length];
        var count = 0;

        foreach (var ch in key)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-')
            {
                chars[count++] = char.ToLowerInvariant(ch);
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                chars[count++] = '_';
            }
        }

        if (count == 0)
            return "unknown";

        return new string(chars, 0, count);
    }
}
