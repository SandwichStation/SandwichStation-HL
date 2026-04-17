using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Tag;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._FarHorizons.Medical.ConditionalHealing;

[Serializable, NetSerializable, DataDefinition]
public sealed partial class ConditionalHealingData
{
    [DataField]
    public DamageSpecifier Damage = default!;
    [DataField]
    public float BloodlossModifier = 0.0f;
    [DataField]
    public float ModifyBloodLevel = 0.0f;
    [DataField]
    public List<string>? DamageContainers;
    [DataField]
    public float Delay = 2f;
    [DataField]
    public float SelfHealPenaltyMultiplier = 2f;
    [DataField]
    public SoundSpecifier? HealingBeginSound = null;
    [DataField]
    public SoundSpecifier? HealingEndSound = null;
    [DataField]
    public int AdjustEyeDamage = 0;
}

[Serializable, NetSerializable, DataDefinition]
public sealed partial class ConditionalHealingDefition
{
    [DataField]
    public HashSet<ProtoId<TagPrototype>> AllowedTags = [];
    [DataField]
    public ConditionalHealingData Healing = default!;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ConditionalHealingComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public List<ConditionalHealingDefition> HealingDefinitions = [];
}