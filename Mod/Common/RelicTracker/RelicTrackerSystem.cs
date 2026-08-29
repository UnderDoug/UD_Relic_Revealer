using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using ConsoleLib.Console;

using HistoryKit;

using Qud.API;
using Qud.UI;

using UD_Relic_Revealer.Mod.Events;

using XRL;
using XRL.Collections;
using XRL.Language;
using XRL.UI;
using XRL.Wish;
using XRL.World;
using XRL.World.Parts;

using SerializeField = UnityEngine.SerializeField;

namespace UD_Relic_Revealer.Mod
{
    [PlayerMutator]
    [HasGameBasedStaticCache]
    [HasCallAfterGameLoaded]
    [HasWishCommand]
    [Serializable]
    public class RelicTrackerSystem
        : IPlayerSystem
        , IPlayerMutator
        , IModEventHandler<AfterZoneActivatedEvent>
    {
        [GameBasedStaticCache(CreateInstance = false)]
        private static RelicTrackerSystem _Instance;
        public static RelicTrackerSystem Instance
        {
            get
            {
                if (_Instance == null)
                    RelicTrackerSystemInit(WorldGen: true);
                return _Instance;
            }
            private set => _Instance = value;
        }

        private static string LastGameID;

        public static Renderable NoRelicsIcon = new(
            Tile: "Abilities/abil_berate.bmp",
            ColorString: $"&K",
            TileColor: $"&K",
            DetailColor: 'R');

        public static IRenderable MissingIcon = new Renderable(
            Tile: "Mutations/amnesia.bmp",
            ColorString: $"&K",
            TileColor: $"&K",
            DetailColor: 'B');

        private static IRenderable _RevealerIcon;
        public static IRenderable RevealerIcon
        {
            get
            {
                if (_RevealerIcon == null)
                {
                    if (GameObjectFactory.Factory is not GameObjectFactory factory
                        || factory.GetBlueprintIfExists("Telescopic Monocle")?.GetRenderable() is not IRenderable telescopicMonocleRender)
                        return MissingIcon;
                    _RevealerIcon = telescopicMonocleRender;
                }
                return _RevealerIcon;
            }
        }

        public static Comparison<RelicRecord> EraTierComparison = delegate (RelicRecord x, RelicRecord y)
        {
            if (x == null
                || y == null)
                return (x == null).CompareTo(y == null);

            bool xUnpin = !x.IsPinned();
            bool yUnpin = !y.IsPinned();
            try
            {
                x.Pin();
                y.Pin();
                if (x.Era.CompareTo(y.Era) is int eraComp
                    && eraComp != 0)
                    return -eraComp;

                if (x.IsMask.CompareTo(y.IsMask) is int maskComp
                    && maskComp != 0)
                    return -maskComp;

                if (x.Tier.CompareTo(y.Tier) is int tierComp
                    && tierComp != 0)
                    return tierComp;

                if ((x.RelicName?.Strip()).CompareTo(y.RelicName?.Strip()) is int relicNameComp
                    && relicNameComp != 0)
                    return relicNameComp;

                return (x.DisplayName?.Strip()).CompareTo(y.DisplayName?.Strip());
            }
            finally
            {
                if (xUnpin)
                    x.Unpin();

                if (yUnpin)
                    y.Unpin();
            }
        };

        protected string GameID;

        private bool _Initialized;
        public bool Initialized
        {
            get => _Initialized;
            protected set
            {
                _Initialized = value;
                if (!value)
                {
                    HasShown = false;
                    _CachedRelicRecords = new(64);
                    _TrackersWantingRecords = new(64);
                }
            }
        }

        private Dictionary<Guid, RelicRecord> _CachedRelicRecords = new(64);

        private Dictionary<UD_RelicTracker, Guid> _TrackersWantingRecords = new(64);

        public IEnumerable<RelicRecord> RelicRecords => GetOrderedRecords();

        public IEnumerable<RelicRecord> ViewableRelicRecords => GetOrderedRecords(IsEligibleToShow);

        public bool HasShown;

        protected bool ProcessedRobberChimesTriggered;

        public RelicTrackerSystem()
        { }

        private static RelicTrackerSystem InitializeSystem(bool Silent)
        {
            if (!Silent)
            {
                Utils.Log($"{nameof(RelicTrackerSystem)}.{nameof(InitializeSystem)} Called...");
                Utils.Log($"  constructing new {nameof(RelicTrackerSystem)}...");
            }

            if (LastGameID.IsNullOrEmpty()
                || The.Game?.GameID != LastGameID)
            {
                var system = new RelicTrackerSystem() { GameID = The.Game.GameID };
                LastGameID = system.GameID;
                return system;
            }

            return new();
        }

        private static RelicTrackerSystem InitializeSystem() => InitializeSystem(Silent: true);

        [CallAfterGameLoaded]
        public static void AfterGameLoaded()
        {
            RelicTrackerSystemInit(WorldGen: false);
        }

        [GameBasedCacheInit]
        public static void RelicTrackerGameBasedCacheInit()
        {
            /*Utils.Info($"{nameof(RelicTrackerSystem)}.{nameof(RelicTrackerGameBasedCacheInit)}...");
            Utils.Log($"  {{{The.Game?.GameID}}} - {nameof(The)}.{nameof(The.Game)}.{nameof(The.Game.GameID)}");
            Utils.Log($"  {{{LastGameID ?? Guid.Empty.ToString()}}} - {nameof(RelicTrackerSystem)}.{nameof(LastGameID)}");
            Utils.Log($"  {{{_Instance?.GameID ?? Guid.Empty.ToString()}}} - {nameof(Instance)}.{nameof(GameID)}");*/
        }

        public static void RelicTrackerSystemInit(bool WorldGen)
        {
            Utils.Info($"{nameof(RelicTrackerSystem)}.{nameof(RelicTrackerSystemInit)}({nameof(WorldGen)}: {WorldGen}) Called...");

            if (The.Game == null)
            {
                Utils.Info($"{nameof(The)}.{nameof(The.Game)} is null.");
                Instance = null;
                return;
            }

            if (WorldGen
                && !LastGameID.IsNullOrEmpty()
                && The.Game.GameID == LastGameID)
            {
                if (_Instance != null)
                    The.Game.RemoveSystem(_Instance);
                Instance = null;
                Utils.Info($"{nameof(The)}.{nameof(The.Game)}.{nameof(The.Game.GameID)} matches {nameof(RelicTrackerSystem)}.{nameof(LastGameID)}... System removed and Instance nulled.");
            }

            _Instance ??= The.Game.GetSystem<RelicTrackerSystem>() ?? The.Game.RequireSystem(InitializeSystem);

            if (_Instance.Initialized)
                _Instance.Initialized = false;

            if (_Instance != null)
            {
                Utils.Info($"{nameof(Instance)} {(!_Instance.Initialized ? "constructed" : "loaded")} and assigned!");
                // Loading.LoadTask($"Tracking Relics", Instance.Init, showToUser: false); // show to user once this does something (if it ever does)
                if (_Instance.Initialized)
                    _Instance.ViewableRelicRecords.Loggregate(
                        Proc: RelicRecord.DebugString,
                        Empty: "no records",
                        PostProc: s => $"  : {s}")
                        ;
            }
            else
                Utils.Error($"Failed to load {nameof(RelicTrackerSystem)}.");
        }

        public void Init()
        {
            TrackRelics();
        }

        public void TrackRelics(bool Silent = true)
        {
            if (!Silent)
                Utils.Log($"{nameof(RelicTrackerSystem)}.{nameof(TrackRelics)}, {nameof(Initialized)}: {Initialized}");
            if (!Initialized)
            {
                _CachedRelicRecords ??= new(64);
                _CachedRelicRecords.Clear();
                _CachedRelicRecords.EnsureCapacity(64);

                _TrackersWantingRecords ??= new(64);
                _TrackersWantingRecords.Clear();
                _TrackersWantingRecords.EnsureCapacity(64);

                if (GenerateOrderedRecords() is IEnumerable<RelicRecord> relicRecords)
                    foreach (var relicRecord in relicRecords)
                        _CachedRelicRecords[relicRecord.TrackerID] = relicRecord;

                ClearInvalid();

                Initialized = !ViewableRelicRecords.IsNullOrEmpty();

                if (Initialized)
                {
                    if (!Silent)
                        Utils.Log($"  Initialization Succeeded...");
                }
                else
                {
                    if (!Silent)
                        Utils.Log($"  Initialization Failed...");
                }

                if (!Silent)
                    ViewableRelicRecords.Loggregate(
                        Proc: RelicRecord.DebugString,
                        Empty: "no records",
                        PostProc: s => $"    : {s}");
            }
            else
            {
                if (!Silent)
                    Utils.Log($"  Already initialized...");
            }
        }

        public void mutate(GameObject player)
        {
            if (Instance.GameID == null)
                Instance.GameID = The.Game.GameID;
            else
            {
                RelicTrackerSystemInit(WorldGen: true);
            }

            Loading.LoadTask($"Tracking Relics", Instance.Init);
        }

        #region Serialization

        public sealed override bool WantFieldReflection => false;

        public override void Write(SerializationWriter Writer)
        {
            Writer.WriteNamedFields(this, GetType());

            Writer.WriteOptimized(GameID);

            Writer.WriteOptimized(_CachedRelicRecords?.Count ?? -1);
            foreach ((var _, var relicRecord) in _CachedRelicRecords.IteratorSafe())
                Writer.WriteComposite(relicRecord);

            Writer.Write(ProcessedRobberChimesTriggered);
        }

        public override void Read(SerializationReader Reader)
        {
            Utils.Log($"{nameof(RelicTrackerSystem)}.{nameof(Read)}");
            Utils.Log($"  {nameof(SerializationReader)}.{nameof(SerializationReader.ReadNamedFields)}");
            Reader.ReadNamedFields(this, GetType());

            Utils.Log($"  {nameof(GameID)}");
            GameID = Reader.ReadOptimizedString();

            Utils.Log($"  {nameof(_CachedRelicRecords)}");
            int count = Reader.ReadOptimizedInt32();
            if (count >= 0)
            {
                _CachedRelicRecords = new(count);
                while (count > 0)
                {
                    var relicRecord = Reader.ReadComposite<RelicRecord>();
                    _CachedRelicRecords[relicRecord.TrackerID] = relicRecord;
                    count--;
                }
            }

            Utils.Log($"  {nameof(ProcessedRobberChimesTriggered)}");
            ProcessedRobberChimesTriggered = Reader.ReadBoolean();

            Utils.Log($"  Read Complete!");
        }

        public override void AfterLoad(XRLGame game)
        {
            Utils.Log($"{nameof(RelicTrackerSystem)}.{nameof(AfterLoad)}");
            base.AfterLoad(game);

            var existingSystem = game.GetSystem<RelicTrackerSystem>();
            if (existingSystem != this)
            {
                if (existingSystem == null)
                    Utils.Warn($"Game lacks loaded {nameof(RelicTrackerSystem)}...");
                else
                {
                    Utils.Warn($"Loaded {nameof(RelicTrackerSystem)} is separate instance from one attached to game...");
                    game.RemoveSystem(existingSystem);
                }
                game.AddSystem(this);
            }

            Utils.Log($"  {nameof(_Instance)} = this");
            _Instance = this;

            Utils.Log($"  {nameof(SyncRelics)}");
            SyncRelics();

            Utils.Log($"  Load Complete!");
        }

        #endregion

        public static bool IsRelic(GameObject Object, int ForReliquary = 0)
        {
            if (Object.GetPropertyOrTag($"{Utils.MOD_ID}.{nameof(RelicTrackerSystem)}.ExcludeRelic", $"{false}").EqualsNoCase($"{true}"))
                return false;

            if (Object.HasPart(nameof(SultanMask)))
                return true;

            if (!Object.HasStringProperty("RelicName"))
                return false;

            if (!Object.TryGetPart(out TakenAchievement takenAch)
                || takenAch.AchievementID != Achievement.RECOVER_RELIC?.ID)
            {
                if (ForReliquary <= 0)
                    return false;
            }

            return true;
        }

        public RelicRecord NewRelicRecord(GameObject Relic, int ForReliquary = 0, int? FromReliquary = null)
            => IsRelic(Relic, FromReliquary ?? ForReliquary)
            ? new RelicRecord(Relic, ForReliquary)
            : null
            ;

        public IEnumerable<RelicRecord> GenerateRelicRecords(IEnumerable<GameObject> Source, int ForReliquary = 0)
        {
            foreach (var gameObject in Source.IteratorSafe())
                if (NewRelicRecord(gameObject, ForReliquary) is RelicRecord relicRecord)
                    yield return relicRecord;
        }

        public IEnumerable<RelicRecord> GenerateZoneCacheRecords()
        {
            foreach (var relicRecord in GenerateRelicRecords(The.ZoneManager?.CachedObjects?.Values))
                yield return relicRecord;
        }

        public IEnumerable<RelicRecord> GenerateReliquaryRecords()
        {
            // Utils.Log($"{nameof(RelicTrackerSystem)}.{nameof(GetReliquaryRelics)}...");
            SultanLoot sultanLoot = new();
            for (int i = 6; i > 0; i--)
            {
                // Utils.Log($"  Sultan Period {i}:");
                sultanLoot.Period = i;
                if (NewRelicRecord(sultanLoot.generateFace(), ForReliquary: i) is RelicRecord maskRecord)
                {
                    // Utils.Log($"      : {relicRecord.DebugString()}");
                    yield return maskRecord;
                }
                /*else
                    Utils.Log($"      : failed to make record");*/

                foreach (string id in (HistoryAPI.GetSultanForPeriod(i)?.GetList("items")).IteratorSafe())
                {
                    // Utils.Log($"    {nameof(id)}: {id}");
                    if (The.Game.sultanHistory.GetEntitiesByDelegate(e => e.GetCurrentSnapshot().Name == id).FirstOrDefault() is not HistoricEntity relicEntity)
                        continue;

                    if (relicEntity.GetCurrentSnapshot() is not HistoricEntitySnapshot relicSnapshot)
                        continue;

                    if (RelicGenerator.GenerateRelic(relicSnapshot, RelicGenerator.GetRelicTierFromPeriod(int.Parse(relicSnapshot.GetProperty("period")))) is not GameObject relic)
                        continue;

                    // Utils.Log($"      : {relic.DebugName ?? "NO_RELIC"}");

                    if (NewRelicRecord(relic, ForReliquary: i) is not RelicRecord relicRecord)
                    {
                        // Utils.Log($"      : failed to make record");
                        continue;
                    }

                    // Utils.Log($"      : {relicRecord.DebugString()}");
                    yield return relicRecord;
                }
            }
        }

        public IEnumerable<RelicRecord> GenerateZoneRecords()
        {
            foreach (var zoneObject in (The.ActiveZone?.YieldObjects()).IteratorSafe())
                foreach (var relicRecord in GenerateRelicRecords(zoneObject.GetObjectsRecursively()))
                    yield return relicRecord;
        }

        private void AddRecordsIfNone(IEnumerable<RelicRecord> Source, IList<RelicRecord> Relics, IList<RelicRecord> Masks)
        {
            foreach (var relicRecord in Source)
            {
                if (Relics.None(r => r.SameAs(relicRecord)))
                {
                    Relics.Add(relicRecord);
                    if (relicRecord.IsMask)
                    {
                        Masks.Add(relicRecord);
                        Utils.Log($"    added (mask): {relicRecord.DebugString()}");
                    }
                    else
                    {
                        Utils.Log($"    added: {relicRecord.DebugString()}");
                    }
                }
                else
                {
                    Utils.Log($"    already exists: {relicRecord.DebugString()}");
                }
            }
        }

        protected IEnumerable<RelicRecord> GenerateOrderedRecords()
        {
            using var relics = ScopeDisposedList<RelicRecord>.GetFromPool();
            using var masks = ScopeDisposedList<RelicRecord>.GetFromPool();
            Utils.Log($"{nameof(RelicTrackerSystem)}.{nameof(GenerateOrderedRecords)}...");
            Utils.Log($"  {nameof(GenerateZoneCacheRecords)}...");
            AddRecordsIfNone(GenerateZoneCacheRecords(), relics, masks);

            Utils.Log($"  {nameof(GenerateReliquaryRecords)}...");
            AddRecordsIfNone(GenerateReliquaryRecords(), relics, masks);

            Utils.Log($"  {nameof(GenerateZoneRecords)}...");
            AddRecordsIfNone(GenerateZoneRecords(), relics, masks);

            Utils.Log($"  {nameof(relics)}.StableSortInPlace...");
            relics.StableSortInPlace(EraTierComparison);

            Utils.Log($"  Remove mask duplicates...");
            foreach (var maskRecord in masks.IteratorSafe())
            {
                if (!maskRecord.IsForReliquary)
                {
                    Utils.Log($"    not for reliquary: {maskRecord.DebugString()}");
                    var relicsToRemove = relics.Where(r => r.IsMask && r.ForReliquary == maskRecord.Era && r.RelicName == maskRecord.RelicName);
                    foreach (var relicToRemove in relicsToRemove)
                    {
                        Utils.Log($"      removing like mask: {relicToRemove.DebugString()}");
                        relics.Remove(relicToRemove);
                    }
                }
                else
                {
                    Utils.Log($"    is for reliquary: {maskRecord.DebugString()}");
                }
            }

            foreach (var relic in relics.IteratorSafe())
            {
                if (!IsEligibleToShow(relic))
                {
                    relic.Dispose();
                    continue;
                }

                yield return relic;
            }

        }

        public IEnumerable<RelicRecord> GetRecords(Predicate<RelicRecord> Where = null)
        {
            foreach (var record in (_CachedRelicRecords?.Values).IteratorSafe())
                if (Where?.Invoke(record) is not false)
                    yield return record;
        }

        public IEnumerable<RelicRecord> GetOrderedRecords(Predicate<RelicRecord> Where = null)
        {
            using var records = RentRecords(Where, EraTierComparison);
            foreach (var record in records)
                yield return record;
        }

        public ScopeDisposedList<RelicRecord> RentRecords(Predicate<RelicRecord> Where = null, Comparison<RelicRecord> Comparison = null)
        {
            var records = ScopeDisposedList<RelicRecord>.GetFromPoolFilledWith(GetRecords(Where));
            if (Comparison != null)
                records.StableSortInPlace(Comparison);

            return records;
        }

        public bool HasRelicRecord(RelicRecord RelicRecord, bool ForSync = false)
        {
            if (RelicRecord == null)
                return false;

            if (ForSync
                && _CachedRelicRecords.TryGetValue(RelicRecord.TrackerID, out var cachedRecord))
            {
                if (cachedRecord == RelicRecord)
                    return true;
            }

            foreach (var relicRecord in _CachedRelicRecords.Values)
            {
                if (relicRecord == RelicRecord)
                    return true;

                if (!ForSync
                    && relicRecord.SameAs(RelicRecord))
                    return true;
            }
            return false;
        }

        public bool RegisterTrackerForSync(UD_RelicTracker RelicTracker)
        {
            _TrackersWantingRecords ??= new();
            _TrackersWantingRecords[RelicTracker] = RelicTracker.TrackerID;
            return _TrackersWantingRecords.ContainsKey(RelicTracker);
        }

        public bool UnregisterTrackerForSync(UD_RelicTracker RelicTracker, bool RemoveTracker = false)
        {
            _TrackersWantingRecords ??= new();
            _TrackersWantingRecords.Remove(RelicTracker);

            if (RemoveTracker)
                RelicTracker?.ParentObject?.RemovePart(RelicTracker);

            return !_TrackersWantingRecords.ContainsKey(RelicTracker);
        }

        public bool SyncRelicRecord(ref RelicRecord RelicRecord, UD_RelicTracker RelicTracker)
        {
            Utils.Log($"{nameof(RelicTrackerSystem)}.{nameof(SyncRelicRecord)} for {RelicTracker?.ParentObject?.DebugName ?? "NO_RELIC"}...");
            if (_CachedRelicRecords.IsNullOrEmpty())
            {
                Utils.Log($"  {nameof(_CachedRelicRecords)} is null, recording tracker in want of record...");
                RegisterTrackerForSync(RelicTracker);
                return false;
            }

            if (!_CachedRelicRecords.TryGetValue(RelicTracker.TrackerID, out RelicRecord))
            {
                Utils.Log($"  {nameof(_CachedRelicRecords)} doesn't contain TrackerID {{{RelicTracker.TrackerID}}}, recording tracker in want of record...");
                RegisterTrackerForSync(RelicTracker);
                return false;
            }

            RelicRecord.ParentTracker = RelicTracker;

            UnregisterTrackerForSync(RelicTracker);
            return true;
        }

        public void SyncRelics()
        {
            Utils.Log($"{nameof(RelicTrackerSystem)}.{nameof(SyncRelics)}...");

            using var relicRecords = RentRecords();
            using var relicTrackers = ScopeDisposedList<UD_RelicTracker>.GetFromPoolFilledWith(_TrackersWantingRecords.Keys);

            Utils.Log($"  {nameof(RelicRecords)} before...");
            /*PinAllRecordsWhile(delegate ()
            {*/
                RelicRecords.Loggregate(
                    Proc: RelicRecord.DebugString,
                    Empty: "no records",
                    PostProc: s => $"    : {s}");
            /*});*/
            
            foreach (var relicTracker in relicTrackers)
                if (!_TrackersWantingRecords.TryGetValue(relicTracker, out var trackerID)
                    || !SyncRelicRecord(ref relicTracker.RelicRecord, relicTracker))
                    UnregisterTrackerForSync(relicTracker);

            foreach (var relicRecord in relicRecords)
            {
                if (relicRecord.ParentTracker == null)
                    if (!relicRecord.IsValidRecord
                        || !relicRecord.SetSynched(this))
                        RemoveRelic(relicRecord, Dispose: true);
            }

            ClearInvalid();

            Utils.Log($"  {nameof(RelicRecords)} after...");
            /*PinAllRecordsWhile(delegate ()
            {*/
                RelicRecords.Loggregate(
                    Proc: RelicRecord.DebugString,
                    Empty: "no records",
                    PostProc: s => $"    : {s}");
            /*});*/
        }

        public void PinAllRecordsWhile(Action Action)
        {
            var recordUnpins = new Dictionary<RelicRecord, bool>();
            foreach ((var _, var record) in _CachedRelicRecords)
                if (recordUnpins[record] = !record.IsPinned())
                    record.Pin();

            Action?.Invoke();

            foreach ((var record, bool unpin) in recordUnpins)
                if (unpin)
                    record.Unpin();
        }

        public RelicRecord GetFirstRecordOrDefault(Predicate<RelicRecord> Where = null)
            => GetRecords(Where).FirstOrDefault()
            ;

        public RelicRecord AddRecord(RelicRecord RelicRecord)
        {
            if (RelicRecord == null)
                return null;

            _CachedRelicRecords[RelicRecord.TrackerID] = RelicRecord;

            if (!RelicRecord.SetSynched(this))
                return null;

            return RelicRecord;
        }

        public RelicRecord RecordRelic(GameObject Relic, RelicRecord SourceRecord = null)
            => AddRecord(new RelicRecord(Relic, SourceRecord))
            ;


        public bool TryRecordRelic(GameObject Relic, out RelicRecord RelicRecord)
            => (RelicRecord = RecordRelic(Relic)) != null
            ;

        public bool TryRecordDuplicateRelic(GameObject Relic, RelicRecord SourceRecord, out RelicRecord RelicRecord)
            => (RelicRecord = RecordRelic(Relic, SourceRecord)) != null
            ;

        public bool RemoveWithID(Guid TrackerID, bool Dispose = false)
        {
            if (_CachedRelicRecords.IsNullOrEmpty())
                return false;

            if (!_CachedRelicRecords.TryGetValue(TrackerID, out var relicRecord))
                return false;

            if (relicRecord.IsPinned())
                return false;

            if (!_CachedRelicRecords.Remove(TrackerID))
                return false;

            if (Dispose)
                relicRecord.Dispose();

            return true;
        }

        public bool RemoveRelic(RelicRecord RelicRecord, bool Dispose = false)
            => RelicRecord != null
            && RemoveWithID(RelicRecord.TrackerID, Dispose)
            ;

        public void ClearInvalid()
        {
            try
            {
                using (var records = RentRecords())
                {
                    using var realMasks = RentRecords(r => r.IsMask && !r.IsForReliquary);
                    foreach (var realMask in realMasks)
                    {
                        try
                        {
                            using (var recordsToRemove = RentRecords(r => r.IsMask && r.ForReliquary == realMask.Era && r.RelicName == realMask.RelicName))
                            {
                                foreach (var recordToRemove in recordsToRemove)
                                {
                                    try
                                    {
                                        RemoveWithID(recordToRemove.TrackerID);
                                        records.Remove(recordToRemove);
                                    }
                                    catch (Exception x)
                                    {
                                        Utils.Warn($"{nameof(RelicTrackerSystem)} ran into issue tidying up non-real record {recordToRemove?.DebugString()}", x);
                                    }
                                }
                            }
                        }
                        catch (Exception x)
                        {
                            Utils.Warn($"{nameof(RelicTrackerSystem)} ran into issue tidying up records for non-real copies of {realMask?.DebugString()}", x);
                        }
                    }
                    foreach (var record in records)
                    {
                        if (!IsEligibleToShow(record))
                        {
                            record.Unpin();
                            RemoveWithID(record.TrackerID);
                        }
                    }
                }
            }
            catch (Exception x)
            {
                Utils.Warn($"{nameof(RelicTrackerSystem)} ran into issue clearing invalid records", x);
            }
        }

        private static bool IsEligibleToShow(RelicRecord RelicRecord)
            => RelicRecord?.IsValidRecord is true
            ;

        public void RevealRelics(bool RethrowOnError = false, bool ForceNoRelics = false, bool Debug = false)
        {
            if (The.Game.GetIntGameState("RobberChimesTriggered") is int robberChimesTriggered
                && robberChimesTriggered > 1)
                ProcessRobberChimesTriggered(TriggeredPeriod: robberChimesTriggered - 1);

            ClearInvalid();

            using var relics = RentRecords(IsEligibleToShow, EraTierComparison);

            if (relics.IsNullOrEmpty()
                || ForceNoRelics)
            {
                Popup.ShowSpace(
                    Message: "There don't appear to be any relics.",
                    Title: "No Relics",
                    AfterRender: NoRelicsIcon
                );
                return;
            }

            using var relicOptions = !Debug
                ? ScopeDisposedList<string>.GetFromPoolFilledWith(relics.Select(RelicRecord.OptionDisplayString))
                : ScopeDisposedList<string>.GetFromPoolFilledWith(relics.Select(RelicRecord.DebugString));

            using var relicRenders = ScopeDisposedList<IRenderable>.GetFromPoolFilledWith(relics.Select(r => r.Render));
            using var relicHotkeys = ScopeDisposedList<char>.GetFromPool();
            foreach (var relic in relics)
                relicHotkeys.Add(relicHotkeys.GetNextHotKey());

            try
            {
                int result = -1;
                do
                {
                    result = Popup.PickOption(
                        Title: "{{W|Relics, Revealed!}}" + (Debug ? "{{W| ({{B|Debug Edition}})}}" : null),
                        Intro: $"Below are the relics that generated for this world.\n\nSelect one to view it as though looking at it{(Debug ? ", including its internals if the GameObject is still present" : null)}.\n\xff",
                        Options: relicOptions,
                        Hotkeys: relicHotkeys,
                        Icons: relicRenders,
                        IntroIcon: RevealerIcon,
                        AllowEscape: true,
                        PopupID: nameof(RelicReveal_WishHandler));

                    if (result >= 0)
                        relics[result].ViewRelic(Internals: Debug);
                }
                while (result >= 0);
            }
            catch (Exception x)
            {
                Utils.Error($"{nameof(RevealRelics)} failed to get cached relics", x);
                if (RethrowOnError)
                    throw x;
            }
        }

        public bool ProcessZoneEvent(IZoneEvent E)
        {
            if (E.Zone is not Zone z)
                return false;

            return ProcessZone(z, E);
        }

        public bool ProcessZoneEvent(AfterZoneActivatedEvent E)
        {
            if (E.Zone is not Zone z)
                return false;

            return ProcessZone(z, E);
        }

        public bool ProcessZone(Zone Z, MinEvent FromEvent = null)
        {
            string zoneProp = $"{nameof(RelicTrackerSystem)}";
            if (FromEvent != null)
                zoneProp += $".{FromEvent?.GetType()?.Name ?? "MISSING_EVENT"}";
            else
                zoneProp += $".{nameof(ProcessZone)}";

            if (Z.GetZoneProperty(zoneProp).EqualsNoCase("true"))
                return true;

            // Utils.Log($"{nameof(RelicTrackerSystem)}.{nameof(ProcessZone)} {nameof(Zone)} {Z?.ZoneID ?? "MISSING_ZONE"} ({FromEvent?.GetType()?.Name ?? "NO_EVENT"})");

            Z.SetZoneProperty(zoneProp, "true");

            SultanLoot sultanLoot = null;
            using var clearedReliquaries = ScopeDisposedList<int>.GetFromPool();
            using var processedRelics = ScopeDisposedList<GameObject>.GetFromPool();
            foreach (var zoneObject in Z.YieldObjects())
            {
                // Utils.Log($"  {nameof(zoneObject)}: {zoneObject?.DebugName ?? "MISSING_OBJECT"}");
                foreach (var recursiveZoneObject in zoneObject.GetObjectsRecursively())
                {
                    // Utils.Log($"    {nameof(recursiveZoneObject)}: {recursiveZoneObject?.DebugName ?? "MISSING_OBJECT"}");
                    if (recursiveZoneObject.InInventory is GameObject zoneObjectContainer)
                    {
                        /*if (zoneObjectContainer.TryGetPart(out DoubleContainer doubleContainer)
                            && !doubleContainer.Master)
                            continue;*/

                        if (sultanLoot == null
                            || !clearedReliquaries.Contains(sultanLoot.Period))
                        {
                            if (zoneObjectContainer.TryGetPart(out sultanLoot))
                            {
                                /*Utils.Log($"      {nameof(zoneObjectContainer)} {zoneObjectContainer.DebugName ?? "MISSING_OBJECT"} has {nameof(SultanLoot)} with {nameof(SultanLoot.Period)} {sultanLoot.Period} ({nameof(sultanLoot.generated)}: {sultanLoot.generated})");*/
                            }
                        }
                    }

                    if (sultanLoot == null
                        || !clearedReliquaries.Contains(sultanLoot.Period))
                    {
                        if (recursiveZoneObject.TryGetPart(out sultanLoot))
                        {
                            /*Utils.Log($"      {nameof(recursiveZoneObject)} {recursiveZoneObject.DebugName ?? "MISSING_OBJECT"} has {nameof(SultanLoot)} with {nameof(SultanLoot.Period)} {sultanLoot.Period} ({nameof(sultanLoot.generated)}: {sultanLoot.generated})");*/
                        }
                    }

                    if (sultanLoot != null
                        && !clearedReliquaries.Contains(sultanLoot.Period)
                        && sultanLoot.generated)
                    {
                        // Utils.Log($"      {nameof(sultanLoot)}.{nameof(sultanLoot.Period)} is {sultanLoot.Period}, removing relevant relics...");
                        using (var relicRecords = RentRecords())
                        {
                            foreach (var relicRecord in relicRecords)
                            {
                                if (relicRecord.ForReliquary == sultanLoot.Period)
                                {
                                    try
                                    {
                                        relicRecord.Unpin();
                                        RemoveRelic(relicRecord);
                                    }
                                    catch (Exception x)
                                    {
                                        Utils.Warn($"{nameof(RelicTrackerSystem)}.{nameof(ProcessZoneEvent)} failed to remove {relicRecord.DebugString()}, {Grammar.Ordinal(sultanLoot.Period)} sultan era relic, after loading its zone {nameof(Zone)} {Z?.ZoneID ?? "MISSING_ZONE"}", x);
                                    }
                                }
                            }
                        }
                        clearedReliquaries.Add(sultanLoot.Period);
                    }

                    if (!processedRelics.Contains(recursiveZoneObject)
                        && NewRelicRecord(recursiveZoneObject, FromReliquary: sultanLoot?.Period) is RelicRecord newRelicRecord)
                    {
                        try
                        {
                            if (!HasRelicRecord(newRelicRecord))
                                AddRecord(newRelicRecord);
                            else
                                newRelicRecord.Dispose();

                            processedRelics.Add(recursiveZoneObject);
                        }
                        catch (Exception x)
                        {
                            Utils.Warn($"{nameof(RelicTrackerSystem)}.{nameof(ProcessZoneEvent)} failed to record {nameof(newRelicRecord)} of {recursiveZoneObject?.DebugName ?? "MISSING_OBJECT"} for {nameof(Zone)} {Z?.ZoneID ?? "MISSING_ZONE"}", x);
                        }
                    }
                }
            }
            return true;
        }

        public bool ProcessRobberChimesTriggered(GameObject TriggeredReliquary = null, int? TriggeredPeriod = null)
        {
            if (ProcessedRobberChimesTriggered)
                return true;

            using var records = RentRecords();

            bool any = false;
            foreach (var relicRecord in records)
                if (relicRecord.ProcessRobberChimesTriggered(TriggeredReliquary, TriggeredPeriod))
                    any = true;

            ClearInvalid();
            ProcessedRobberChimesTriggered = any;
            return any;
        }

        public override void Register(XRLGame Game, IEventRegistrar Registrar)
        {
            Registrar.Register(AfterZoneActivatedEvent.ID, EventOrder.EXTREMELY_LATE);
            base.Register(Game, Registrar);
        }

        public override void RegisterPlayer(GameObject Player, IEventRegistrar Registrar)
        {
            Registrar.Register(BeforeTakeActionEvent.ID, EventOrder.EXTREMELY_LATE);
            // Registrar.Register(ZoneBuiltEvent.ID, EventOrder.EXTREMELY_LATE);
            // Registrar.Register(ZoneActivatedEvent.ID, EventOrder.EXTREMELY_LATE);
            base.RegisterPlayer(Player, Registrar);
        }

        public override bool HandleEvent(BeforeTakeActionEvent E)
        {
            try
            {
                if (!HasShown)
                    Instance.RevealRelics();
            }
            finally
            {
                HasShown = true;
                The.Player?.UnregisterEvent(this, BeforeTakeActionEvent.ID);
            }
            return base.HandleEvent(E);
        }

        public override bool HandleEvent(ZoneBuiltEvent E)
        {
            ProcessZoneEvent(E);
            return base.HandleEvent(E);
        }

        public virtual bool HandleEvent(AfterZoneActivatedEvent E)
        {
            // Utils.Log($"{nameof(RelicTrackerSystem)}.{nameof(HandleEvent)}({nameof(AfterZoneActivatedEvent)} E)...");
            ProcessZoneEvent(E);
            return base.HandleEvent(E);
        }

        public override bool HandleEvent(ZoneActivatedEvent E)
        {
            ProcessZoneEvent(E);
            return base.HandleEvent(E);
        }

        #region Wishes

        [WishCommand(Command = "UD revealrelics")]
        public static bool RelicReveal_WishHandler()
        {
            try
            {
                Instance.RevealRelics(RethrowOnError: true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        [WishCommand(Command = "UD revealrelics none")]
        public static bool RelicReveal_None_WishHandler()
        {
            try
            {
                Instance.RevealRelics(RethrowOnError: true, ForceNoRelics: true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        [WishCommand(Command = "UD revealrelics debug")]
        public static bool RelicReveal_Debug_WishHandler()
        {
            try
            {
                Instance.RevealRelics(RethrowOnError: true, Debug: true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion
    }
}
