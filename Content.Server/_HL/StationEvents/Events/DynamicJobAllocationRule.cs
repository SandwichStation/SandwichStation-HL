using System;
using Content.Server._NF.Roles.Systems;
using Content.Server.GameTicking;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Server.StationEvents.Components;
using Content.Shared._NF.Roles.Components;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Shared.Mind.Components;
using JetBrains.Annotations;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Player;

namespace Content.Server.StationEvents.Events;

[UsedImplicitly]
public sealed class DynamicJobAllocationRule : StationEventSystem<DynamicJobAllocationRuleComponent>
{
    [Dependency] private readonly StationJobsSystem _stationJobs = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    private bool _recalculationQueued;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeLocalEvent<PlayerJoinedLobbyEvent>(OnPlayerJoinedLobby);
        SubscribeLocalEvent<JobTrackingStateChangedEvent>(OnJobTrackingStateChanged);
        SubscribeLocalEvent<MindAddedMessage>(OnMindAddedGlobal, after: new[] { typeof(JobTrackingSystem) });
        SubscribeLocalEvent<MindRemovedMessage>(OnMindRemovedGlobal, after: new[] { typeof(JobTrackingSystem) });
        _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _playerManager.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    protected override void Started(EntityUid uid, DynamicJobAllocationRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);
        QueueRecalculation();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_recalculationQueued)
            return;

        _recalculationQueued = false;
        UpdateActiveRules();
    }

    protected override void ActiveTick(EntityUid uid, DynamicJobAllocationRuleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, component, gameRule, frameTime);

        component.TimeSinceLastCheck += frameTime;

        if (component.TimeSinceLastCheck >= component.CheckInterval)
        {
            component.TimeSinceLastCheck = 0f;
            AdjustJobSlots(uid, component);
        }
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent ev)
    {
        QueueRecalculation();
    }

    private void OnJobTrackingStateChanged(JobTrackingStateChangedEvent ev)
    {
        QueueRecalculation();
    }

    private void OnPlayerJoinedLobby(PlayerJoinedLobbyEvent ev)
    {
        QueueRecalculation();
    }

    private void OnMindAddedGlobal(MindAddedMessage ev)
    {
        QueueRecalculation();
    }

    private void OnMindRemovedGlobal(MindRemovedMessage ev)
    {
        QueueRecalculation();
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs e)
    {
        QueueRecalculation();
    }

    private void QueueRecalculation()
    {
        _recalculationQueued = true;
    }

    private void UpdateActiveRules()
    {
        var query = QueryActiveRules();
        while (query.MoveNext(out var uid, out _, out var component, out _))
        {
            AdjustJobSlots(uid, component);
        }
    }

    private void AdjustJobSlots(EntityUid uid, DynamicJobAllocationRuleComponent component)
    {
        var stations = new Dictionary<EntityUid, (int staffedNonMercenary, int filledMercenary)>();
        var staffedEntities = new HashSet<EntityUid>();

        var stationQuery = EntityQueryEnumerator<StationJobsComponent>();
        while (stationQuery.MoveNext(out var stationUid, out _))
        {
            stations[stationUid] = (0, 0);
        }

        if (stations.Count == 0)
            return;

        foreach (var session in _playerManager.Sessions)
        {
            if (session.AttachedEntity is not { } attached)
                continue;

            if (session.Status != SessionStatus.InGame)
                continue;

            staffedEntities.Add(attached);
        }

        var jobsQuery = EntityQueryEnumerator<JobTrackingComponent>();
        while (jobsQuery.MoveNext(out var jobTrackedEntity, out var jobTracking))
        {
            if (!staffedEntities.Contains(jobTrackedEntity)
                || jobTracking.Job is not { } job
                || !stations.TryGetValue(jobTracking.SpawnStation, out var counts))
                continue;

            if (job == component.MercenaryJob)
                counts.filledMercenary++;
            else
                counts.staffedNonMercenary++;

            stations[jobTracking.SpawnStation] = counts;
        }

        foreach (var (stationUid, counts) in stations)
        {
            // Skip stations where the mercenary job has been manually overridden via the Station Records Interface.
            if (_stationJobs.IsJobManuallyOverridden(stationUid, component.MercenaryJob))
                continue;

            var desiredTotalSlots = Math.Min(counts.staffedNonMercenary, component.MercenaryCap);
            var availableSlots = Math.Max(0, desiredTotalSlots - counts.filledMercenary);

            _stationJobs.TrySetJobMidRoundMax(stationUid, component.MercenaryJob, desiredTotalSlots);

            _stationJobs.TrySetJobSlot(stationUid, component.MercenaryJob, availableSlots);
        }
    }
}
