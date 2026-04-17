using Robust.Shared.GameStates;

namespace Content.Shared.Drugs;

/// <summary>
///     Exists for use as a status effect. Adds a subtle dark overlay to the client that provides atmospheric enhancement without combat impairment.
///     Used by the Widow narcotic for immersive abyssal power effects.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class AbyssalWhispersComponent : Component { }