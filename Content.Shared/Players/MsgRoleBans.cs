using Content.Shared.Roles;
using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Players;

/// <summary>
/// Sent server -> client to inform the client of their role bans.
/// </summary>
public sealed class MsgRoleBans : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.EntityEvent;

    public List<ProtoId<JobPrototype>> JobBans = new();
    public List<ProtoId<AntagPrototype>> AntagBans = new();

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        JobBans.Clear();
        var jobCount = buffer.ReadVariableInt32();
        for (var i = 0; i < jobCount; i++)
        {
            JobBans.Add(new ProtoId<JobPrototype>(buffer.ReadString()));
        }

        AntagBans.Clear();
        var antagCount = buffer.ReadVariableInt32();
        for (var i = 0; i < antagCount; i++)
        {
            AntagBans.Add(new ProtoId<AntagPrototype>(buffer.ReadString()));
        }
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.WriteVariableInt32(JobBans.Count);
        foreach (var ban in JobBans)
        {
            buffer.Write(ban.Id);
        }

        buffer.WriteVariableInt32(AntagBans.Count);
        foreach (var ban in AntagBans)
        {
            buffer.Write(ban.Id);
        }
    }
}
