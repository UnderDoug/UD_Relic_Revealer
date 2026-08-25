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

        public static IRenderable NoRelicsIcon = new Renderable(
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

        public static Comparison<RelicRecord> TierComparison = delegate (RelicRecord x, RelicRecord y)
        {
            if (x == null
                || y == null)
                return (x == null).CompareTo(y == null);

            if (x.Tier.CompareTo(y.Tier) is int tierComp
                && tierComp != 0)
                return tierComp;

            return (x?.DisplayName?.Strip()).CompareTo(y?.DisplayName?.Strip());
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
                    _CachedRelicRecords = null;
                }
            }
        }

        private List<RelicRecord> _CachedRelicRecords;
        public IEnumerable<RelicRecord> CachedRelicRecords
        {
            get
            {
                if (_CachedRelicRecords == null)
                {
                    Utils.Log($"get_{nameof(CachedRelicRecords)}; {nameof(_CachedRelicRecords)} is null...");
                    _CachedRelicRecords = new();
                    if (GetOrderedRelics() is IEnumerable<RelicRecord> orderedRecords)
                        _CachedRelicRecords.AddRange(orderedRecords);
                }

                /*if (!_CachedRelicRecords.IsNullOrEmpty())
                {
                    using var relicRecords = ScopeDisposedList<RelicRecord>.GetFromPoolFilledWith(_CachedRelicRecords);
                    foreach (var relicRecord in relicRecords)
                        if (!IsEligibleToShow(relicRecord))
                            RemoveRelic(relicRecord);
                }*/

                return _CachedRelicRecords;
            }
        }

        public bool HasShown;

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
                    _Instance.CachedRelicRecords.Loggregate(
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
                _CachedRelicRecords = null;
                Initialized = !CachedRelicRecords.IsNullOrEmpty();
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
                    CachedRelicRecords.Loggregate(
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

            Writer.WriteComposite(_CachedRelicRecords);
        }

        public override void Read(SerializationReader Reader)
        {
            Utils.Log($"{nameof(RelicTrackerSystem)}.{nameof(Read)}");
            Utils.Log($"  {nameof(SerializationReader)}.{nameof(SerializationReader.ReadNamedFields)}");
            Reader.ReadNamedFields(this, GetType());

            Utils.Log($"  {nameof(_CachedRelicRecords)}");
            _CachedRelicRecords = Reader.ReadCompositeList<RelicRecord>();

            Utils.Log($"  Read Complete!");
        }

        public override void AfterLoad(XRLGame game)
        {
            Utils.Log($"{nameof(RelicTrackerSystem)}.{nameof(AfterLoad)}");
            base.AfterLoad(game);
            Utils.Log($"  {nameof(SyncRelics)}");
            SyncRelics();
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

            Utils.Log($"  Load Complete!");
        }

        #endregion

        public static bool IsRelic(GameObject Object, int ForReliquary = 0)
        {
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

        public IEnumerable<RelicRecord> GetRelicRecords(IEnumerable<GameObject> Source, int ForReliquary = 0)
        {
            foreach (var gameObject in Source.IteratorSafe())
                if (NewRelicRecord(gameObject, ForReliquary) is RelicRecord relicRecord)
                    yield return relicRecord;
        }

        public IEnumerable<RelicRecord> GetCacheRelics()
        {
            foreach (var relicRecord in GetRelicRecords(The.ZoneManager?.CachedObjects?.Values))
                yield return relicRecord;
        }

        public IEnumerable<RelicRecord> GetReliquaryRelics()
        {
            Utils.Log($"{nameof(RelicTrackerSystem)}.{nameof(GetReliquaryRelics)}...");
            for (int i = 6; i > 0; i--)
            {
                using var relics = ScopeDisposedList<GameObject>.GetFromPool();
                Utils.Log($"  Sultan Period {i}:");
                foreach (string id in HistoryAPI.GetSultanForPeriod(i).GetList("items"))
                {
                    Utils.Log($"    {nameof(id)}: {id}");
                    if (The.Game.sultanHistory.GetEntitiesByDelegate(e => e.GetCurrentSnapshot().Name == id).FirstOrDefault() is not HistoricEntity relicEntity)
                        continue;

                    if (relicEntity.GetCurrentSnapshot() is not HistoricEntitySnapshot relicSnapshot)
                        continue;

                    if (RelicGenerator.GenerateRelic(relicSnapshot, RelicGenerator.GetRelicTierFromPeriod(int.Parse(relicSnapshot.GetProperty("period")))) is not GameObject relic)
                        continue;

                    Utils.Log($"      : {relic.DebugName ?? "NO_RELIC"}");

                    if (NewRelicRecord(relic, ForReliquary: i) is not RelicRecord relicRecord)
                    {
                        Utils.Log($"      : failed to make record");
                        continue;
                    }

                    Utils.Log($"      : {relicRecord.DebugString()}");
                    yield return relicRecord;
                }
            }
        }

        public IEnumerable<RelicRecord> GetZoneRelics()
        {
            foreach (var zoneObject in (The.ActiveZone?.YieldObjects()).IteratorSafe())
                foreach (var relicRecord in GetRelicRecords(zoneObject.GetObjectsRecursively()))
                    yield return relicRecord;
        }

        public IEnumerable<RelicRecord> GetOrderedRelics()
        {
            using var relics = ScopeDisposedList<RelicRecord>.GetFromPool();
            foreach (var relicRecord in GetCacheRelics())
                if (relics.None(r => r.SameAs(relicRecord)))
                    relics.Add(relicRecord);

            foreach (var relicRecord in GetReliquaryRelics())
                if (relics.None(r => r.SameAs(relicRecord)))
                    relics.Add(relicRecord);

            foreach (var relicRecord in GetZoneRelics())
                if (relics.None(r => r.SameAs(relicRecord)))
                    relics.Add(relicRecord);

            relics.StableSortInPlace(TierComparison);

            foreach (var relic in relics.IteratorSafe())
                yield return relic;
        }

        public IEnumerable<RelicRecord> GetRecords(Predicate<RelicRecord> Where)
        {
            foreach (var record in CachedRelicRecords.IteratorSafe())
                if (Where?.Invoke(record) is not false)
                    yield return record;
        }

        public bool HasRelicRecord(RelicRecord RelicRecord, bool ForSync = false)
        {
            if (RelicRecord == null)
                return false;

            return ForSync
                ? _CachedRelicRecords?.Contains(RelicRecord) is true
                : _CachedRelicRecords?.Any(r => r.SameAs(RelicRecord)) is true
                ;
        }

        public bool SyncRelicRecord(RelicRecord RelicRecord)
        {
            Utils.Log($"{nameof(RelicTrackerSystem)}.{nameof(SyncRelicRecord)} for {RelicRecord?.DebugString() ?? "NO_RECORD"}...");
            if (CachedRelicRecords is not IEnumerable<RelicRecord> relicRecords)
            {
                Utils.Log($"  {nameof(CachedRelicRecords)} is null...");
                return false;
            }

            if (relicRecords?.FirstOrDefault(r => r.SameAs(RelicRecord)) is RelicRecord existingRecord)
            {
                Utils.Log($"  {nameof(existingRecord)}: {existingRecord.DebugString()}");
                if (existingRecord == RelicRecord)
                {
                    Utils.Log($"  {nameof(RelicRecord)} is {nameof(existingRecord)}");
                    return true;
                }

                existingRecord.Unpin();
                Utils.Log($"    {nameof(existingRecord)} unpinned");

                RemoveRelic(existingRecord);
                Utils.Log($"    {nameof(existingRecord)} removed");

                existingRecord.Dispose();
                Utils.Log($"    {nameof(existingRecord)} disposed");
            }
            else
                Utils.Log($"  {nameof(existingRecord)}: none");

            _CachedRelicRecords.Add(RelicRecord);
            Utils.Log($"  {nameof(RelicRecord)} added to cache");
            _CachedRelicRecords.StableSortInPlace(TierComparison);
            return true;
        }

        public bool SyncRelic(UD_RelicTracker RelicTracker)
        {
            Utils.Log($"{nameof(RelicTrackerSystem)}.{nameof(SyncRelic)} for {nameof(RelicTracker)} of {RelicTracker?.ParentObject?.DebugName ?? "NO_RELIC"}...");
            if (CachedRelicRecords is not IEnumerable<RelicRecord> relicRecords)
            {
                Utils.Log($"  {nameof(CachedRelicRecords)} is null...");
                return false;
            }

            if (RelicTracker.ParentObject is not GameObject relic)
            {
                Utils.Log($"  {nameof(RelicTracker)}.{nameof(RelicTracker.ParentObject)} is null...");
                return false;
            }

            if (relicRecords?.FirstOrDefault(r => r.SameAs(relic)) is RelicRecord existingRecord)
            {
                Utils.Log($"  {nameof(existingRecord)}: {existingRecord.DebugString()}");
                if (existingRecord == RelicTracker.RelicRecord)
                {
                    Utils.Log($"  {nameof(RelicTracker)}.{nameof(RelicTracker.RelicRecord)} is {nameof(existingRecord)}");
                    return true;
                }

                RelicTracker.RelicRecord?.Dispose();
                Utils.Log($"    {nameof(RelicTracker)}.{nameof(RelicTracker.RelicRecord)} disposed");

                RelicTracker.RelicRecord = existingRecord;
                Utils.Log($"    {nameof(RelicTracker)}.{nameof(RelicTracker.RelicRecord)} assigned");

                RelicTracker.RelicRecord.Unpin(RefreshRelic: true);
                Utils.Log($"    {nameof(RelicTracker)}.{nameof(RelicTracker.RelicRecord)} unpinned (refreshed)");
                return true;
            }
            else
                Utils.Log($"  {nameof(existingRecord)}: none");

            return SyncRelicRecord(RelicTracker.RelicRecord);
        }

        public void SyncRelics()
        {
            Utils.Log($"{nameof(RelicTrackerSystem)}.{nameof(SyncRelics)}...");
            using var relicRecords = ScopeDisposedList<RelicRecord>.GetFromPoolFilledWith(CachedRelicRecords);

            Utils.Log($"  {nameof(CachedRelicRecords)} before...");
            CachedRelicRecords.Loggregate(
                Proc: RelicRecord.DebugString,
                Empty: "no records",
                PostProc: s => $"    : {s}");

            foreach (var relicRecord in relicRecords)
            {
                if (relicRecord.Relic?.GetPart<UD_RelicTracker>() is UD_RelicTracker relicTracker)
                    SyncRelic(relicTracker);
                else
                if (!IsEligibleToShow(relicRecord))
                    RemoveRelic(relicRecord);
            }

            _CachedRelicRecords?.StableSortInPlace(TierComparison);

            Utils.Log($"  {nameof(CachedRelicRecords)} after...");
            CachedRelicRecords.Loggregate(
                Proc: RelicRecord.DebugString,
                Empty: "no records",
                PostProc: s => $"    : {s}");
        }

        public RelicRecord GetFirstRecordOrDefault(Predicate<RelicRecord> Where = null)
            => GetRecords(Where).FirstOrDefault()
            ;

        public RelicRecord FindRecordFor(GameObject Relic)
            => Relic != null
            ? GetFirstRecordOrDefault(r => r.Relic == Relic)
            : null
            ;

        public bool TryFindRecordFor(GameObject Relic, out RelicRecord RelicRecord)
            => (RelicRecord = FindRecordFor(Relic)) != null
            ;

        public RelicRecord AddRecord(RelicRecord RelicRecord)
        {
            if (RelicRecord == null)
                return null;

            _CachedRelicRecords ??= new();
            _CachedRelicRecords.Add(RelicRecord);

            if (!RelicRecord.SetSynched(this))
                return null;

            _CachedRelicRecords.StableSortInPlace(TierComparison);
            return RelicRecord;
        }

        public RelicRecord RequireRecord(RelicRecord RelicRecord, bool Dispose = true)
        {
            if (RelicRecord == null)
                throw new ArgumentNullException(nameof(RelicRecord), "Cannot be null");

            if (_CachedRelicRecords?.FirstOrDefault(r => r.SameAs(RelicRecord)) is RelicRecord existingRecord)
            {
                if (existingRecord.SetSynched(this))
                {
                    if (Dispose)
                        RelicRecord.Dispose();
                    return existingRecord;
                }
                else
                {
                    existingRecord.Unpin();
                    RemoveRelic(existingRecord);

                    if (Dispose)
                        existingRecord.Dispose();
                }
            }
            return AddRecord(RelicRecord);
        }

        public RelicRecord RecordRelic(GameObject Relic, RelicRecord SourceRecord = null)
        {
            if (FindRecordFor(Relic) != null)
                return null;

            var record = AddRecord(new RelicRecord(Relic, SourceRecord));
            return record;
        }

        public bool TryRecordRelic(GameObject Relic, out RelicRecord RelicRecord)
            => (RelicRecord = RecordRelic(Relic)) != null
            ;

        public bool TryRecordDuplicateRelic(GameObject Relic, RelicRecord SourceRecord, out RelicRecord RelicRecord)
            => (RelicRecord = RecordRelic(Relic, SourceRecord)) != null
            ;

        public bool RemoveRelic(RelicRecord RelicRecord, bool Dispose = false)
        {
            if (RelicRecord?.IsPinned() is not true
                && _CachedRelicRecords?.Remove(RelicRecord) is true)
            {
                if (Dispose)
                    RelicRecord.Dispose();

                return true;
            }

            return false;
        }

        public bool RemoveRelic(GameObject Relic)
            => FindRecordFor(Relic) is RelicRecord record
            && (_CachedRelicRecords?.Remove(record) is true)
            ;

        private static bool IsEligibleToShow(RelicRecord RelicRecord)
            => (RelicRecord?.IsRemainingCached is true)
            || (RelicRecord?.HasValidRelic is true)
            ;

        public void RevealRelics(bool RethrowOnError = false)
        {
            _CachedRelicRecords?.StableSortInPlace(TierComparison);

            if (CachedRelicRecords is not IEnumerable<RelicRecord> relicRecords
                || relicRecords.IsNullOrEmpty())
            {
                Popup.NewPopupMessageAsync(
                    message: "There don't appear to be any relics.",
                    buttons: PopupMessage.SingleButton,
                    contextTitle: "No Relics",
                    contextRender: NoRelicsIcon
                ).Wait();
                return;
            }

            using var relics = ScopeDisposedList<RelicRecord>.GetFromPoolFilledWith(relicRecords);
            using var relicOptions = ScopeDisposedList<string>.GetFromPoolFilledWith(relics.Select(RelicRecord.OptionDisplayString));
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
                        Title: "{{W|Relics, Revealed!}}",
                        Intro: "Below are the relics that generated for this world.\n\nSelect one to view it as though looking at it.\n\xff",
                        Options: relicOptions,
                        Hotkeys: relicHotkeys,
                        Icons: relicRenders,
                        IntroIcon: RevealerIcon,
                        AllowEscape: true,
                        PopupID: nameof(RelicReveal_WishHandler));

                    if (result >= 0)
                        relics[result].ViewRelic();
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

            //Utils.Log($"{nameof(RelicTrackerSystem)}.{nameof(ProcessZone)} {nameof(Zone)} {Z?.ZoneID ?? "MISSING_ZONE"} ({FromEvent?.GetType()?.Name ?? "NO_EVENT"})");

            Z.SetZoneProperty(zoneProp, "true");

            SultanLoot sultanLoot = null;
            using var clearedReliquaries = ScopeDisposedList<int>.GetFromPool();
            using var processedRelics = ScopeDisposedList<GameObject>.GetFromPool();
            foreach (var zoneObject in Z.YieldObjects())
            {
                //Utils.Log($"  {nameof(zoneObject)}: {zoneObject?.DebugName ?? "MISSING_OBJECT"}");
                foreach (var recursiveZoneObject in zoneObject.GetObjectsRecursively())
                {
                    //Utils.Log($"    {nameof(recursiveZoneObject)}: {recursiveZoneObject?.DebugName ?? "MISSING_OBJECT"}");
                    if (recursiveZoneObject.InInventory is GameObject zoneObjectContainer)
                    {
                        /*if (zoneObjectContainer.TryGetPart(out DoubleContainer doubleContainer)
                            && !doubleContainer.Master)
                            continue;*/

                        if (sultanLoot == null
                            || !clearedReliquaries.Contains(sultanLoot.Period))
                        {
                            //if (zoneObjectContainer.TryGetPart(out sultanLoot))
                                //Utils.Log($"      {nameof(zoneObjectContainer)} {zoneObjectContainer.DebugName ?? "MISSING_OBJECT"} has {nameof(SultanLoot)} with {nameof(SultanLoot.Period)} {sultanLoot.Period} ({nameof(sultanLoot.generated)}: {sultanLoot.generated})");
                        }
                    }

                    if (sultanLoot == null
                        || !clearedReliquaries.Contains(sultanLoot.Period))
                    {
                        //if (recursiveZoneObject.TryGetPart(out sultanLoot))
                            //Utils.Log($"      {nameof(recursiveZoneObject)} {recursiveZoneObject.DebugName ?? "MISSING_OBJECT"} has {nameof(SultanLoot)} with {nameof(SultanLoot.Period)} {sultanLoot.Period} ({nameof(sultanLoot.generated)}: {sultanLoot.generated})");
                    }

                    if (sultanLoot != null
                        && !clearedReliquaries.Contains(sultanLoot.Period)
                        && sultanLoot.generated)
                    {
                        //Utils.Log($"      {nameof(sultanLoot)}.{nameof(sultanLoot.Period)} is {sultanLoot.Period}, removing relevant relics...");
                        using (var relicRecords = ScopeDisposedList<RelicRecord>.GetFromPoolFilledWith(CachedRelicRecords))
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

        public override void Register(XRLGame Game, IEventRegistrar Registrar)
        {
            Registrar.Register(AfterZoneActivatedEvent.ID, EventOrder.EXTREMELY_LATE);
            base.Register(Game, Registrar);
        }

        public override void RegisterPlayer(GameObject Player, IEventRegistrar Registrar)
        {
            Registrar.Register(BeforeTakeActionEvent.ID, EventOrder.EXTREMELY_LATE);
            // Registrar.Register(ZoneBuiltEvent.ID, EventOrder.EXTREMELY_LATE);
            //Registrar.Register(ZoneActivatedEvent.ID, EventOrder.EXTREMELY_LATE);
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

        #endregion
    }
}
