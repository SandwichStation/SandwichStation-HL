using System.Diagnostics.CodeAnalysis;
using Content.Server._HL.ColComm; // HardLight
using Content.Server._NF.SectorServices; // Frontier
using Content.Server.Access.Systems;
using Content.Server.Forensics;
using Content.Shared.Access.Components;
using Content.Shared.Forensics.Components;
using Content.Shared.GameTicking;
using Content.Shared.Humanoid; // HardLight
using Content.Shared.Inventory;
using Content.Shared._NF.Roles.Components; // HardLight
using Content.Shared.PDA;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.StationRecords;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using System.Linq;

namespace Content.Server.StationRecords.Systems;

/// <summary>
///     Station records.
///
///     A station record is tied to an ID card, or anything that holds
///     a station record's key. This key will determine access to a
///     station record set's record entries, and it is imperative not
///     to lose the item that holds the key under any circumstance.
///
///     Records are mostly a roleplaying tool, but can have some
///     functionality as well (i.e., security records indicating that
///     a specific person holding an ID card with a linked key is
///     currently under warrant, showing a crew manifest with user
///     settable, custom titles).
///
///     General records are tied into this system, as most crewmembers
///     should have a general record - and most systems should probably
///     depend on this general record being created. This is subject
///     to change.
/// </summary>
public sealed class StationRecordsSystem : SharedStationRecordsSystem
{
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly StationRecordKeyStorageSystem _keyStorage = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IdCardSystem _idCard = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SectorServiceSystem _sectorService = default!; // Frontier
    [Dependency] private readonly ForensicsSystem _forensics = default!; // Frontier

    static readonly ProtoId<JobPrototype>[] FakeJobIds = ["Contractor", "Pilot", "Mercenary"]; // Frontier

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawn);
        SubscribeLocalEvent<EntityRenamedEvent>(OnRename);
        SubscribeLocalEvent<ColcommRegistryRoundStartEvent>(OnColcommRoundStart); // HardLight
    }

    private void OnPlayerSpawn(PlayerSpawnCompleteEvent args)
    {
        CreateGeneralRecord(args.Station, args.Mob, args.Profile, args.JobId); // HardLight: Removed stationRecords
    }

    private void OnRename(ref EntityRenamedEvent ev)
    {
        // When a player gets renamed their card gets changed to match.
        // Unfortunately this means that an event is called for it as well, and since TryFindIdCard will succeed if the
        // given entity is a card and the card itself is the key the record will be mistakenly renamed to the card's name
        // if we don't return early.
        // We also do not include the PDA itself being renamed, as that triggers the same event (e.g. for chameleon PDAs).
        if (HasComp<IdCardComponent>(ev.Uid) ||  HasComp<PdaComponent>(ev.Uid))
            return;

        if (_idCard.TryFindIdCard(ev.Uid, out var idCard))
        {
            if (TryComp(idCard, out StationRecordKeyStorageComponent? keyStorage)
                && keyStorage.Key is {} key)
            {
                if (TryGetRecord<GeneralStationRecord>(key, out var generalRecord))
                {
                    generalRecord.Name = ev.NewName;
                }

                Synchronize(key);
            }
        }
    }

    /// <summary>
    /// HardLight: Gets the single authoritative records store used by cross-round consoles and systems.
    /// </summary>
    public bool TryGetAuthoritativeRecords(
        out EntityUid stationUid,
        [NotNullWhen(true)] out StationRecordsComponent? stationRecords)
    {
        stationUid = _sectorService.GetServiceEntity();
        if (stationUid == EntityUid.Invalid || !TryComp<StationRecordsComponent>(stationUid, out stationRecords))
        {
            stationUid = EntityUid.Invalid;
            stationRecords = null;
            return false;
        }

        return true;
    }

    public void CreateGeneralRecord(EntityUid station, EntityUid player, HumanoidCharacterProfile profile,
        string? jobId) // HardLight: Removed StationRecordsComponent records
    {
        // TODO make PlayerSpawnCompleteEvent.JobId a ProtoId
        if (string.IsNullOrEmpty(jobId)
            || !_prototypeManager.HasIndex<JobPrototype>(jobId))
            return;

        if (!_inventory.TryGetSlotEntity(player, "id", out var idUid))
            return;

        TryComp<FingerprintComponent>(player, out var fingerprintComponent);
        TryComp<DnaComponent>(player, out var dnaComponent);

        /// Frontier start: Generate sector-wide station record
        if (TryComp<SpecialSectorStationRecordComponent>(player, out var specialRecord) && specialRecord.RecordGeneration == RecordGenerationType.NoRecord)
            return;

        if (!TryGetAuthoritativeRecords(out var serviceEnt, out var stationRecords))
            return;

        // HardLight: Checks if certain information should be faked, if so, fake it.
        string playerJob = jobId;
        string? fingerprint = fingerprintComponent?.Fingerprint;
        string? dna = dnaComponent?.DNA;
        if (specialRecord != null
            && specialRecord.RecordGeneration == RecordGenerationType.FalseRecord)
        {
            playerJob = _random.Pick(FakeJobIds);
            fingerprint = _forensics.GenerateFingerprint();
            dna = _forensics.GenerateDNA();
        }

        CreateGeneralRecord(serviceEnt, idUid.Value, profile.Name, profile.Age, profile.Species, profile.Gender, playerJob, fingerprint, dna, profile, stationRecords);

        // HardLight: Mirror the record key onto the character so lifecycle cleanup can remove stale records
        // even when the ID card moves away or is deleted separately.
        if (TryComp<StationRecordKeyStorageComponent>(idUid.Value, out var keyStorage)
            && keyStorage.Key is { } key)
        {
            SetEntityKey(player, key);
        }
        /// Frontier end
    }


    /// <summary>
    ///     Create a general record to store in a station's record set.
    /// </summary>
    /// <remarks>
    ///     This is tied into the record system, as any crew member's
    ///     records should generally be dependent on some generic
    ///     record with the bare minimum of information involved.
    /// </remarks>
    /// <param name="station">The entity uid of the station.</param>
    /// <param name="idUid">The entity uid of an entity's ID card. Can be null.</param>
    /// <param name="name">Name of the character.</param>
    /// <param name="species">Species of the character.</param>
    /// <param name="gender">Gender of the character.</param>
    /// <param name="jobId">
    ///     The job to initially tie this record to. This must be a valid job loaded in, otherwise
    ///     this call will cause an exception. Ensure that a general record starts out with a job
    ///     that is currently a valid job prototype.
    /// </param>
    /// <param name="mobFingerprint">Fingerprint of the character.</param>
    /// <param name="dna">DNA of the character.</param>
    ///
    /// <param name="profile">
    ///     Profile for the related player. This is so that other systems can get further information
    ///     about the player character.
    ///     Optional - other systems should anticipate this.
    /// </param>
    /// <param name="records">Station records component.</param>
    public void CreateGeneralRecord(
        EntityUid station,
        EntityUid? idUid,
        string name,
        int age,
        string species,
        Gender gender,
        string jobId,
        string? mobFingerprint,
        string? dna,
        HumanoidCharacterProfile profile,
        StationRecordsComponent records)
    {
        if (!_prototypeManager.TryIndex<JobPrototype>(jobId, out var jobPrototype))
            throw new ArgumentException($"Invalid job prototype ID: {jobId}");

        // when adding a record that already exists use the old one
        // this happens when respawning as the same character
        if (GetRecordByName(station, name, records) is {} id)
        {
            // HardLight start
            // Reuse and refresh existing record fields when respawning as the same character.
            var existingKey = new StationRecordKey(id, station);

            if (TryGetRecord<GeneralStationRecord>(existingKey, out var existingRecord, records))
            {
                existingRecord.Name = name;
                existingRecord.Age = age;
                existingRecord.Species = species;
                existingRecord.Gender = gender;
                existingRecord.JobTitle = jobPrototype.LocalizedName;
                existingRecord.JobIcon = jobPrototype.Icon;
                existingRecord.JobPrototype = jobId;
                existingRecord.DisplayPriority = jobPrototype.RealDisplayWeight;
                existingRecord.Fingerprint = mobFingerprint;
                existingRecord.DNA = dna;

                RaiseLocalEvent(new RecordModifiedEvent(existingKey));
            }
            // HardLight end

            SetIdKey(idUid, existingKey); // HardLight
            return;
        }

        var record = new GeneralStationRecord()
        {
            Name = name,
            Age = age,
            JobTitle = jobPrototype.LocalizedName,
            JobIcon = jobPrototype.Icon,
            JobPrototype = jobId,
            Species = species,
            Gender = gender,
            DisplayPriority = jobPrototype.RealDisplayWeight,
            Fingerprint = mobFingerprint,
            DNA = dna
        };

        var key = AddRecordEntry(station, record);
        if (!key.IsValid())
        {
            Log.Warning($"Failed to add general record entry for {name}");
            return;
        }

        SetIdKey(idUid, key);

        RaiseLocalEvent(new AfterGeneralRecordCreatedEvent(key, record, profile));
    }

    /// <summary>
    /// Set the station records key for an id/pda.
    /// </summary>
    public void SetIdKey(EntityUid? uid, StationRecordKey key)
    {
        if (uid is not {} idUid)
            return;

        var keyStorageEntity = idUid;
        if (TryComp<PdaComponent>(idUid, out var pda) && pda.ContainedId is {} id)
        {
            keyStorageEntity = id;
        }

        _keyStorage.AssignKey(keyStorageEntity, key);
    }

    /// <summary>
    /// HardLight: Set the station records key for an entity that should be treated as the character owner of a record.
    /// </summary>
    public void SetEntityKey(EntityUid uid, StationRecordKey key)
    {
        _keyStorage.AssignKey(uid, key);
    }

    /// <summary>
    /// HardLight: At round restart, clear stale records and rebuild from all currently-active tracked crew.
    /// This ensures persisted characters appear immediately without waiting for a spawn event.
    /// </summary>
    private void OnColcommRoundStart(ColcommRegistryRoundStartEvent ev)
    {
        if (!TryGetAuthoritativeRecords(out var authority, out var authorityRecords))
            return;

        // Clear all existing records in O(1) by replacing the set.
        authorityRecords.Records = new StationRecordSet();

        // Rebuild from every currently active tracked crew member.
        var jobQuery = AllEntityQuery<JobTrackingComponent>();
        while (jobQuery.MoveNext(out var uid, out var job))
        {
            if (!job.Active || job.Job is not { } jobId)
                continue;

            if (!_prototypeManager.TryIndex<JobPrototype>(jobId, out var jobPrototype))
                continue;

            var name = MetaData(uid).EntityName;
            if (name.Length == 0)
                continue;

            TryComp<HumanoidAppearanceComponent>(uid, out var humanoid);
            TryComp<FingerprintComponent>(uid, out var fingerprint);
            TryComp<DnaComponent>(uid, out var dna);

            var record = new GeneralStationRecord()
            {
                Name = name,
                Age = humanoid?.Age ?? 18,
                JobTitle = jobPrototype.LocalizedName,
                JobIcon = jobPrototype.Icon,
                JobPrototype = jobId,
                Species = humanoid?.Species ?? "Human",
                Gender = humanoid?.Gender ?? Gender.Epicene,
                DisplayPriority = jobPrototype.RealDisplayWeight,
                Fingerprint = fingerprint?.Fingerprint,
                DNA = dna?.DNA,
            };

            var id = authorityRecords.Records.AddRecordEntry(record);
            if (id == null)
                continue;

            SetEntityKey(uid, new StationRecordKey(id.Value, authority));
        }
    }

    /// <summary>
    /// Removes a record from this station.
    /// </summary>
    /// <param name="key">The station and key to remove.</param>
    /// <param name="records">Station records component.</param>
    /// <returns>True if the record was removed, false otherwise.</returns>
    public bool RemoveRecord(StationRecordKey key, StationRecordsComponent? records = null)
    {
        // HardLight: Prefer authoritative dataset for all removals.
        if (TryGetAuthoritativeRecords(out var authority, out var authorityRecords))
        {
            var authoritativeKey = key.OriginStation == authority ? key : new StationRecordKey(key.Id, authority);
            if (authorityRecords.Records.RemoveAllRecords(authoritativeKey.Id))
            {
                RaiseLocalEvent(new RecordRemovedEvent(authoritativeKey));
                return true;
            }
        }

        // HardLight: Backward compatibility fallback.
        if (!Resolve(key.OriginStation, ref records))
            return false;

        // HardLight start: Slightly edited.
        if (!records.Records.RemoveAllRecords(key.Id))
            return false;

        RaiseLocalEvent(new RecordRemovedEvent(key));
        return true;
        // HardLight end
    }

    /// <summary>
    /// Gets a random record from the station's record entries.
    /// </summary>
    /// <param name="ent">The EntityId of the station from which you want to get the record.</param>
    /// <param name="entry">The resulting entry.</param>
    /// <typeparam name="T">Type to get from the record set.</typeparam>
    /// <returns>True if a record was obtained. False otherwise.</returns>
    public bool TryGetRandomRecord<T>(Entity<StationRecordsComponent?> ent, [NotNullWhen(true)] out T? entry)
    {
        entry = default;

        if (!Resolve(ent.Owner, ref ent.Comp))
            return false;

        if (ent.Comp.Records.Keys.Count == 0)
            return false;

        var key = _random.Pick(ent.Comp.Records.Keys);

        return ent.Comp.Records.TryGetRecordEntry(key, out entry);
    }

    /// <summary>
    /// Returns an id if a record with the same name exists.
    /// </summary>
    /// <remarks>
    /// Linear search so O(n) time complexity.
    /// </remarks>
    public uint? GetRecordByName(EntityUid station, string name, StationRecordsComponent? records = null)
    {
        if (TryGetAuthoritativeRecords(out var authority, out var authorityRecords))
        {
            station = authority;
            records = authorityRecords;
        }
        else if (!Resolve(station, ref records, false))
        {
            return null;
        }

        foreach (var (id, record) in GetRecordsOfType<GeneralStationRecord>(station, records))
        {
            if (record.Name == name)
                return id;
        }

        return null;
    }

    /// <summary>
    /// Get the name for a record, or an empty string if it has no record.
    /// </summary>
    public string RecordName(StationRecordKey key)
    {
        if (!TryGetRecord<GeneralStationRecord>(key, out var record))
           return string.Empty;

        return record.Name;
    }

    /// <summary>
    /// Adds a new record entry to a station's record set.
    /// </summary>
    /// <param name="station">The station to add the record to.</param>
    /// <param name="record">The record to add.</param>
    /// <param name="records">Station records component.</param>
    /// <typeparam name="T">The type of record to add.</typeparam>
    public StationRecordKey AddRecordEntry<T>(EntityUid station, T record, StationRecordsComponent? records = null)
    {
        // HardLight: Authoritative writes first.
        if (TryGetAuthoritativeRecords(out var authority, out var authorityRecords))
        {
            var id = authorityRecords.Records.AddRecordEntry(record);
            if (id == null)
                return StationRecordKey.Invalid;

            return new StationRecordKey(id.Value, authority);
        }

        if (!Resolve(station, ref records))
            return StationRecordKey.Invalid;

        var fallbackId = records.Records.AddRecordEntry(record);
        if (fallbackId == null)
            return StationRecordKey.Invalid;

        return new StationRecordKey(fallbackId.Value, station);
    }

    /// <summary>
    /// Adds a record to an existing entry.
    /// </summary>
    /// <param name="key">The station and id of the existing entry.</param>
    /// <param name="record">The record to add.</param>
    /// <param name="records">Station records component.</param>
    /// <typeparam name="T">The type of record to add.</typeparam>
    public void AddRecordEntry<T>(StationRecordKey key, T record,
        StationRecordsComponent? records = null)
    {
        // HardLight: Authoritative writes first.
        if (TryGetAuthoritativeRecords(out var authority, out var authorityRecords))
        {
            var authoritativeKey = key.OriginStation == authority ? key : new StationRecordKey(key.Id, authority);
            authorityRecords.Records.AddRecordEntry(authoritativeKey.Id, record);
            return;
        }

        if (!Resolve(key.OriginStation, ref records))
            return;

        records.Records.AddRecordEntry(key.Id, record);
    }

    /// <summary>
    ///     Synchronizes a station's records with any systems that need it.
    /// </summary>
    /// <param name="station">The station to synchronize any recently accessed records with..</param>
    /// <param name="records">Station records component.</param>
    public void Synchronize(EntityUid station, StationRecordsComponent? records = null)
    {
        // HardLight: Authoritative synchronization first.
        if (TryGetAuthoritativeRecords(out var authority, out var authorityRecords))
        {
            foreach (var key in authorityRecords.Records.GetRecentlyAccessed())
            {
                RaiseLocalEvent(new RecordModifiedEvent(new StationRecordKey(key, authority)));
            }

            authorityRecords.Records.ClearRecentlyAccessed();
            return;
        }

        if (!Resolve(station, ref records))
            return;

        foreach (var key in records.Records.GetRecentlyAccessed())
        {
            RaiseLocalEvent(new RecordModifiedEvent(new StationRecordKey(key, station)));
        }

        records.Records.ClearRecentlyAccessed();
    }

    /// <summary>
    /// Synchronizes a single record's entries for a station.
    /// </summary>
    /// <param name="key">The station and id of the record</param>
    /// <param name="records">Station records component.</param>
    public void Synchronize(StationRecordKey key, StationRecordsComponent? records = null)
    {
        // HardLight: Authoritative synchronization first.
        if (TryGetAuthoritativeRecords(out var authority, out var authorityRecords))
        {
            var authoritativeKey = key.OriginStation == authority ? key : new StationRecordKey(key.Id, authority);
            RaiseLocalEvent(new RecordModifiedEvent(authoritativeKey));
            authorityRecords.Records.RemoveFromRecentlyAccessed(authoritativeKey.Id);
            return;
        }

        if (!Resolve(key.OriginStation, ref records))
            return;

        RaiseLocalEvent(new RecordModifiedEvent(key));

        records.Records.RemoveFromRecentlyAccessed(key.Id);
    }

    #region Console system helpers

    /// <summary>
    /// Checks if a record should be skipped given a filter.
    /// Takes general record since even if you are using this for e.g. criminal records,
    /// you don't want to duplicate basic info like name and dna.
    /// Station records lets you do this nicely with multiple types having their own data.
    /// </summary>
    public bool IsSkipped(StationRecordsFilter? filter, GeneralStationRecord someRecord)
    {
        // if nothing is being filtered, show everything
        if (filter == null)
            return false;
        if (filter.Value.Length == 0)
            return false;

        var filterLowerCaseValue = filter.Value.ToLower();

        return filter.Type switch
        {
            StationRecordFilterType.Name =>
                !someRecord.Name.ToLower().Contains(filterLowerCaseValue),
            StationRecordFilterType.Prints => someRecord.Fingerprint != null
                && IsFilterWithSomeCodeValue(someRecord.Fingerprint, filterLowerCaseValue),
            StationRecordFilterType.DNA => someRecord.DNA != null
                && IsFilterWithSomeCodeValue(someRecord.DNA, filterLowerCaseValue),
            _ => throw new IndexOutOfRangeException(nameof(filter.Type)),
        };
    }

    private bool IsFilterWithSomeCodeValue(string value, string filter)
    {
        return !value.ToLower().StartsWith(filter);
    }

    /// <summary>
    /// Build a record listing of id to name for a station and filter.
    /// </summary>
    public Dictionary<uint, string> BuildListing(Entity<StationRecordsComponent> station, StationRecordsFilter? filter)
    {
        var listing = new Dictionary<uint, string>();

        // HardLight start: Build listings from the authoritative dataset whenever available.
        var records = TryGetAuthoritativeRecords(out var authority, out var authorityRecords)
            ? GetRecordsOfType<GeneralStationRecord>(authority, authorityRecords)
            : GetRecordsOfType<GeneralStationRecord>(station, station.Comp);
        // HardLight end

        foreach (var pair in records)
        {
            if (IsSkipped(filter, pair.Item2))
                continue;

            listing.Add(pair.Item1, pair.Item2.Name);
        }

        return listing;
    }

    // HardLight: Resolve lookups against authoritative records while keeping legacy station-local keys readable.
    public bool TryGetRecord<T>(StationRecordKey key, [NotNullWhen(true)] out T? entry, StationRecordsComponent? records = null)
    {
        entry = default;

        // First try the provided/original key so legacy station-local keys keep working during migration.
        if (Resolve(key.OriginStation, ref records, false)
            && records.Records.TryGetRecordEntry(key.Id, out entry))
            return true;

        if (!TryGetAuthoritativeRecords(out var authority, out var authorityRecords))
            return false;

        if (key.OriginStation == authority)
            return false;

        var authoritativeKey = new StationRecordKey(key.Id, authority);
        return authorityRecords.Records.TryGetRecordEntry(authoritativeKey.Id, out entry);
    }

    // HardLight: Enumerate from authoritative records first so manifests are unified across all consumers.
    public IEnumerable<(uint, T)> GetRecordsOfType<T>(EntityUid station, StationRecordsComponent? records = null)
    {
        if (TryGetAuthoritativeRecords(out var authority, out var authorityRecords))
            return authorityRecords.Records.GetRecordsOfType<T>();

        if (!Resolve(station, ref records, false))
            return Enumerable.Empty<(uint, T)>();

        return records.Records.GetRecordsOfType<T>();
    }

    #endregion
}

/// <summary>
/// Base event for station record events
/// </summary>
public abstract class StationRecordEvent : EntityEventArgs
{
    public readonly StationRecordKey Key;
    public EntityUid Station => Key.OriginStation;

    protected StationRecordEvent(StationRecordKey key)
    {
        Key = key;
    }
}

/// <summary>
///     Event raised after the player's general profile is created.
///     Systems that modify records on a station would have more use
///     listening to this event, as it contains the character's record key.
///     Also stores the general record reference, to save some time.
/// </summary>
public sealed class AfterGeneralRecordCreatedEvent : StationRecordEvent
{
    public readonly GeneralStationRecord Record;
    /// <summary>
    /// Profile for the related player. This is so that other systems can get further information
    ///     about the player character.
    ///     Optional - other systems should anticipate this.
    /// </summary>
    public readonly HumanoidCharacterProfile Profile;

    public AfterGeneralRecordCreatedEvent(StationRecordKey key, GeneralStationRecord record,
        HumanoidCharacterProfile profile) : base(key)
    {
        Record = record;
        Profile = profile;
    }
}

/// <summary>
///     Event raised after a record is removed. Only the key is given
///     when the record is removed, so that any relevant systems/components
///     that store record keys can then remove the key from their internal
///     fields.
/// </summary>
public sealed class RecordRemovedEvent : StationRecordEvent
{
    public RecordRemovedEvent(StationRecordKey key) : base(key)
    {
    }
}

/// <summary>
///     Event raised after a record is modified. This is to
///     inform other systems that records stored in this key
///     may have changed.
/// </summary>
public sealed class RecordModifiedEvent : StationRecordEvent
{
    public RecordModifiedEvent(StationRecordKey key) : base(key)
    {
    }
}
