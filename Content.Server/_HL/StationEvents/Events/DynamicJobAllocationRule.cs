using System;
using Content.Server._HL.ColComm; // HardLight
using Content.Server._NF.Roles.Systems;
using Content.Server.GameTicking;
using Content.Server.Station.Systems; // HardLight
using Content.Server.StationEvents.Components;
using Content.Shared._NF.Roles.Components;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Shared.Mind; // HardLight
using Content.Shared.Mind.Components;
using Content.Shared.Roles; // HardLight
using JetBrains.Annotations;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Prototypes; // HardLight

namespace Content.Server.StationEvents.Events;

[UsedImplicitly]
public sealed class DynamicJobAllocationRule : StationEventSystem<DynamicJobAllocationRuleComponent>
{
    [Dependency] private readonly StationJobsSystem _stationJobs = default!;
    [Dependency] private readonly ColcommJobSystem _colcommJobs = default!; // HardLight
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    // HardLight: Legacy job id used before Mercenary/Freelancer naming cleanup.
    private const string LegacyFreelancerJobId = "Freelancer";

    private bool _recalculationQueued;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeLocalEvent<PlayerJoinedLobbyEvent>(OnPlayerJoinedLobby);
        SubscribeLocalEvent<JobTrackingStateChangedEvent>(OnJobTrackingStateChanged);
        SubscribeLocalEvent<JobTrackingComponent, ComponentShutdown>(OnTrackedJobShutdown); // HardLight
        SubscribeLocalEvent<VisitingMindComponent, ComponentInit>(OnVisitingMindAdded); // HardLight
        SubscribeLocalEvent<VisitingMindComponent, ComponentShutdown>(OnVisitingMindRemoved); // HardLight
        SubscribeLocalEvent<MindAddedMessage>(OnMindAddedGlobal, after: new[] { typeof(JobTrackingSystem) });
        SubscribeLocalEvent<MindRemovedMessage>(OnMindRemovedGlobal, after: new[] { typeof(JobTrackingSystem) });
        _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;

        SubscribeLocalEvent<StationInitializedEvent>(OnStationInitialized); // HardLight
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

    // HardLight start
    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent ev) => QueueRecalculation();
    private void OnJobTrackingStateChanged(JobTrackingStateChangedEvent ev) => QueueRecalculation();
    private void OnTrackedJobShutdown(EntityUid uid, JobTrackingComponent component, ref ComponentShutdown args) => QueueRecalculation();
    private void OnVisitingMindAdded(EntityUid uid, VisitingMindComponent component, ref ComponentInit args) => QueueRecalculation();
    private void OnVisitingMindRemoved(EntityUid uid, VisitingMindComponent component, ref ComponentShutdown args) => QueueRecalculation();
    private void OnPlayerJoinedLobby(PlayerJoinedLobbyEvent ev) => QueueRecalculation();
    private void OnMindAddedGlobal(MindAddedMessage ev) => QueueRecalculation();
    private void OnMindRemovedGlobal(MindRemovedMessage ev) => QueueRecalculation();
    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs e) => QueueRecalculation();
    private void OnStationInitialized(StationInitializedEvent ev) => QueueRecalculation();

    private void QueueRecalculation() => _recalculationQueued = true;
    // HardLight end

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
        // HardLight: ColComm registry must exist before we can update slots.
        if (!_colcommJobs.TryGetColcommRegistry(out var colcomm))
            return;

        // HardLight start
        /// <summary>
        /// Count non-Mercenary crew and filled Mercenary slots across the whole server.
        /// This is mind-based rather than attached-entity based so admins who aghost
        /// still count as occupying their role until their mind actually leaves the body.
        /// Using ColComm's configured job list as the filter ensures ship/vessel jobs
        /// such as Contractor and Pilot do not inflate the Mercenary cap.
        /// </summary>
        var totalNonMercenary = 0;
        var totalFilledMercenary = 0;

        var jobsQuery = EntityQueryEnumerator<JobTrackingComponent, MindContainerComponent>();
        while (jobsQuery.MoveNext(out _, out var jobTracking, out var mindContainer))
        {
            if (!jobTracking.Active
                || jobTracking.Job is not { } job)
                continue;

            if (!mindContainer.HasMind
                || !TryComp<MindComponent>(mindContainer.Mind, out var mind)
                || mind.UserId is not { } userId
                || !_playerManager.TryGetSessionById(userId, out var session)
                || session.Status != SessionStatus.InGame)
                continue;

            if (IsMercenaryJob(job, component))
                totalFilledMercenary++;
            else if (_colcommJobs.IsConfiguredJob(colcomm, job))
                totalNonMercenary++;
        }

        var desiredTotal = Math.Min(totalNonMercenary, component.MercenaryCap);
        var availableSlots = Math.Max(0, desiredTotal - totalFilledMercenary);

        // Update ColComm registry (authoritative for tracking).
        _colcommJobs.TrySetJobMidRoundMax(colcomm, component.MercenaryJob, desiredTotal, createSlot: true);
        _colcommJobs.TrySetJobSlot(colcomm, component.MercenaryJob, availableSlots, createSlot: true);

        // Mirror to every physical station's StationJobsComponent for lobby display.
        var stationQuery = EntityQueryEnumerator<Station.Components.StationJobsComponent>();
        while (stationQuery.MoveNext(out var stationUid, out _))
        // HardLight end
        {
            // Skip stations where the mercenary job has been manually overridden via the Station Records Interface.
            if (_stationJobs.IsJobManuallyOverridden(stationUid, component.MercenaryJob))
                continue;

            var desiredTotalSlots = Math.Max(component.MercenaryMinimum, Math.Min(totalNonMercenary, component.MercenaryCap));
            var availableSlotsForStation = Math.Max(0, desiredTotalSlots - totalFilledMercenary);

            _stationJobs.TrySetJobMidRoundMax(stationUid, component.MercenaryJob, desiredTotalSlots);

            _stationJobs.TrySetJobSlot(stationUid, component.MercenaryJob, availableSlotsForStation);
        }
    }

    // HardLight: Count both current and legacy freelancer ids as mercenary-equivalent for slot accounting.
    private static bool IsMercenaryJob(ProtoId<JobPrototype> jobId, DynamicJobAllocationRuleComponent component)
    {
        return string.Equals(jobId, component.MercenaryJob, StringComparison.Ordinal)
               || string.Equals(jobId, LegacyFreelancerJobId, StringComparison.Ordinal);
    }
}
