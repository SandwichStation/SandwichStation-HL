using Content.Shared._Hardlight.Drugs;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;

namespace Content.Server._Hardlight.Drugs;

/// <summary>
/// Server-side system that handles the actual packet transformation
/// </summary>
public sealed class PacketSealingSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solution = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedStorageSystem _storage = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;

    public override void Initialize()
    {
        base.Initialize();
        
        SubscribeLocalEvent<PacketSealingComponent, PacketSealDoAfterEvent>(OnSealDoAfter);
    }

    private void OnSealDoAfter(Entity<PacketSealingComponent> ent, ref PacketSealDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        // Validate we can still seal (reagent check)
        if (!CanSealPacket(ent))
            return;

        SealPacket(ent, args.User);
        args.Handled = true;
    }

    private bool CanSealPacket(Entity<PacketSealingComponent> ent)
    {
        if (ent.Comp.State == PacketState.Sealed)
            return false;

        if (!_solution.TryGetSolution(ent.Owner, ent.Comp.SolutionName, out var solutionEnt, out var solution))
            return false;

        // Check if solution is at full capacity
        if (solution.Volume < solution.MaxVolume)
            return false;

        // Check if it's a single valid drug
        if (solution.Contents.Count != 1)
            return false;

        var reagent = solution.Contents[0];
        return GetWrappedPacketId(reagent.Reagent.Prototype) != null;
    }

    private void SealPacket(Entity<PacketSealingComponent> ent, EntityUid user)
    {
        if (IsUnavailable(ent.Owner) || IsUnavailable(user))
            return;

        if (!_solution.TryGetSolution(ent.Owner, ent.Comp.SolutionName, out var solutionEnt, out var solution))
            return;

        if (solution.Contents.Count != 1)
            return;

        var reagent = solution.Contents[0];
        var wrappedPacketId = GetWrappedPacketId(reagent.Reagent.Prototype);
        if (wrappedPacketId == null)
            return;

        // Play seal sound before transformation
        if (ent.Comp.SealSound != null)
            _audio.PlayPvs(ent.Comp.SealSound, Transform(ent.Owner).Coordinates);

        // Spawn the new wrapped packet
        var wrappedPacket = Spawn(wrappedPacketId, Transform(ent.Owner).Coordinates);

        // Try to place the new packet where the old one was
        if (!TryPlaceInSameLocation(ent.Owner, wrappedPacket, user))
        {
            // If we can't place it in the same spot, just drop it at user's feet
            Transform(wrappedPacket).Coordinates = Transform(user).Coordinates;
        }

        // Remove the empty packet
        Del(ent.Owner);
    }

    /// <summary>
    /// Tries to place the new packet in the same location as the old one (hand, storage, etc.)
    /// </summary>
    private bool TryPlaceInSameLocation(EntityUid oldPacket, EntityUid newPacket, EntityUid user)
    {
        if (IsUnavailable(oldPacket) || IsUnavailable(newPacket) || IsUnavailable(user))
            return false;

        var oldCoordinates = Transform(oldPacket).Coordinates;
        
        // Check if it's in user's hands  
        if (_hands.IsHolding(user, oldPacket, out var handName))
        {
            // Use proper container transfer - remove from hand into a temporary container,
            // then pickup the new item into the same hand
            _hands.TryDrop(user, oldPacket);
            return _hands.TryPickup(user, newPacket, handName);
        }

        // Check if it's in a container (bag, backpack, etc.)
        if (_container.TryGetContainingContainer(oldPacket, out var container))
        {
            if (IsUnavailable(container.Owner))
                return false;

            // For storage containers, try to maintain the exact grid position
            if (container.ID == StorageComponent.ContainerId && 
                TryComp<StorageComponent>(container.Owner, out var storage))
            {
                // Get the storage location of the old packet
                ItemStorageLocation? oldLocation = null;
                if (storage.StoredItems.TryGetValue(oldPacket, out var location))
                {
                    oldLocation = location;
                }

                // Remove old packet
                _container.Remove(oldPacket, container);

                // If we had a specific location, try to insert at the same spot
                if (oldLocation.HasValue)
                {
                    if (_storage.ItemFitsInGridLocation((newPacket, null), (container.Owner, storage), oldLocation.Value))
                    {
                        _storage.TrySetItemStorageLocation((newPacket, null), (container.Owner, storage), oldLocation.Value);
                        return _container.Insert(newPacket, container);
                    }
                }

                // Fallback: insert normally if we can't preserve exact location
                return _container.Insert(newPacket, container);
            }
            
            // For non-storage containers (like hand containers), just insert normally
            _container.Remove(oldPacket, container);
            return _container.Insert(newPacket, container);
        }

        // If it's not in hands or container, it's on the ground/world
        // Keep it in the exact same position
        Transform(newPacket).Coordinates = oldCoordinates;
        return true;
    }

    private string? GetWrappedPacketId(string reagentId)
    {
        return reagentId switch
        {
            "Bake" => "WrappedBakePackage",
            "Rust" => "WrappedRustPackage", 
            "Grit" => "WrappedGritPackage",
            "Breakout" => "WrappedBreakoutPackage",
            "Widow" => "WrappedWidowPackage",
            _ => null
        };
    }

    private bool IsUnavailable(EntityUid uid)
    {
        return TerminatingOrDeleted(uid);
    }
}