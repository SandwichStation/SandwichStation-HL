using System.Linq;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.Actions.Events;

namespace Content.Server.Abilities.Borgs;

public sealed partial class FabricateActionsSystem : EntitySystem
{
    [Dependency] private readonly ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FabricateActionsComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<FabricateActionsComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<FabricateActionsComponent, FabricateActionEvent>(OnFabricate);
    }

    private void OnStartup(Entity<FabricateActionsComponent> entity, ref ComponentStartup args)
    {
        if (TerminatingOrDeleted(entity))
            return;

        foreach (var actionId in entity.Comp.Actions)
        {
            EntityUid? actionEntity = null;
            if (!_actions.AddAction(entity, ref actionEntity, actionId) || actionEntity == null)
                continue;

            if (TerminatingOrDeleted(actionEntity.Value))
                continue;

            entity.Comp.ActionEntities[actionId] = actionEntity.Value;
        }
    }

    private void OnShutdown(Entity<FabricateActionsComponent> entity, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(entity))
            return;

        foreach (var (actionId, actionEntity) in entity.Comp.ActionEntities.ToArray())
        {
            if (actionEntity is not { Valid: true })
                continue;

            if (TerminatingOrDeleted(actionEntity))
            {
                entity.Comp.ActionEntities.Remove(actionId);
                continue;
            }

            _actions.RemoveAction(entity, actionEntity);
            entity.Comp.ActionEntities.Remove(actionId);
        }
    }

    private void OnFabricate(Entity<FabricateActionsComponent> entity, ref FabricateActionEvent args)
    {
        if (args.Handled || !_actionBlocker.CanConsciouslyPerformAction(entity))
            return;

        SpawnNextToOrDrop(args.Fabrication, entity);
        args.Handled = true;
    }
}
