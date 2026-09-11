using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using ConsoleLib.Console;

using HistoryKit;

using Qud.API;

using XRL;
using XRL.Collections;
using XRL.Language;
using XRL.World.Text;
using XRL.UI;
using XRL.Wish;
using XRL.World;
using XRL.World.Parts;
using static XRL.World.Parts.ActivatedAbilities;

using UD_Relic_Revealer.Mod.Events;
using UD_Relic_Revealer.Mod.UI;

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
            ColorString: $"&k",
            TileColor: $"&k",
            DetailColor: 'B');

        public static IRenderable RevealerIcon = new Renderable(
            Tile: "revealer_popup.png",
            ColorString: $"&Y",
            TileColor: $"&Y",
            DetailColor: 'B');

        private static IRenderable _RelicsIcon;
        public static IRenderable RelicsIcon
            => (_RelicsIcon ??= XmlData.DataByCommand.GetValueOrDefault(UD_Player_RelicRevealer.RevealRelicsCommand)?.UITiles?.GetValueOrDefault(XmlData.UITileStates.Default)
                ?? GameObjectFactory.Factory?.GetBlueprintIfExists("The Kesil Face")?.GetRenderable())
            ?? MissingIcon
            ;

        private static List<GameObjectBlueprint> _CherubBlueprints;
        public static IEnumerable<GameObjectBlueprint> CherubBlueprints
        {
            get
            {
                if (_CherubBlueprints.IsNullOrEmpty())
                {
                    _CherubBlueprints ??= new();
                    foreach (var blueprint in (GameObjectFactory.Factory?.BlueprintList).IteratorSafe())
                        if (blueprint.Name.EndsWith("Cherub")
                            && !blueprint.IsBaseBlueprint())
                            _CherubBlueprints.Add(blueprint);
                }
                return _CherubBlueprints;
            }
        }
        public static IRenderable CherubimIcon
            => XmlData.DataByCommand.GetValueOrDefault(UD_Player_RelicRevealer.RevealCherubimCommand)?.UITiles?.GetValueOrDefault(XmlData.UITileStates.Default)
            ?? CherubBlueprints.GetRandomElementCosmetic().GetRenderable()
            ;

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

                if (string.Compare(x.RelicName?.Strip(), y.RelicName?.Strip()) is int relicNameComp
                    && relicNameComp != 0)
                    return relicNameComp;

                return string.Compare(x.DisplayName?.Strip(), y.DisplayName?.Strip());
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
                    _CachedCherubRecords = new(12);
                }
            }
        }

        private Dictionary<Guid, RelicRecord> _CachedRelicRecords = new(64);


        private Dictionary<UD_RelicTracker, Guid> _TrackersWantingRecords = new(64);

        public IEnumerable<RelicRecord> RelicRecords => GetOrderedRecords();

        public IEnumerable<RelicRecord> ViewableRelicRecords => GetOrderedRecords(IsEligibleToShow);

        private List<CherubRecord> _CachedCherubRecords = new(12);
        public IEnumerable<CherubRecord> CherubRecords
        {
            get
            {
                if (_CachedCherubRecords.Count == 0)
                {
                    foreach ((var period, var variant) in CherubRecord.GetCherubPeriodVariantPairs())
                    {
                        var cherubRecord = new CherubRecord(period, variant);
                        if (!cherubRecord.IsValid
                            || _CachedCherubRecords.Contains(cherubRecord))
                        {
                            cherubRecord.Dispose();
                            continue;
                        }
                        _CachedCherubRecords.Add(cherubRecord);
                    }
                    _CachedCherubRecords.StableSortInPlace();
                }
                return _CachedCherubRecords;
            }
        }

        public bool HasShown;

        protected bool ProcessedRobberChimesTriggered;

        protected bool ZoneWantsProcessing;

        public RelicTrackerSystem()
        { }

        private static RelicTrackerSystem InitializeSystem()
        {
            if (LastGameID.IsNullOrEmpty()
                || The.Game?.GameID != LastGameID)
            {
                var system = new RelicTrackerSystem() { GameID = The.Game.GameID };
                LastGameID = system.GameID;
                return system;
            }

            return new();
        }

        [CallAfterGameLoaded]
        public static void AfterGameLoaded()
        {
            RelicTrackerSystemInit(WorldGen: false);
            UD_Player_RelicRevealer.HasShown = false;
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
                /*if (_Instance.Initialized)
                    _Instance.ViewableRelicRecords.Loggregate(
                        Proc: RelicRecord.DebugString,
                        Empty: "no records",
                        PostProc: s => $"  : {s}")
                        ;*/
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

                if (!Silent)
                {
                    if (Initialized)
                        Utils.Log($"  Initialization Succeeded...");
                    else
                        Utils.Log($"  Initialization Failed...");

                    /*ViewableRelicRecords.Loggregate(
                        Proc: RelicRecord.DebugString,
                        Empty: "no records",
                        PostProc: s => $"    : {s}");*/
                }
            }
            else
            {
                if (!Silent)
                    Utils.Log($"  Already initialized...");
            }
        }

        public void mutate(GameObject player)
        {
            player?.RequirePart<UD_Player_RelicRevealer>();

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
            Writer.Write(ZoneWantsProcessing);
        }

        public override void Read(SerializationReader Reader)
        {
            Reader.ReadNamedFields(this, GetType());

            GameID = Reader.ReadOptimizedString();

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
            ProcessedRobberChimesTriggered = Reader.ReadBoolean();
            ZoneWantsProcessing = Reader.ReadBoolean();
        }

        public override void AfterLoad(XRLGame game)
        {
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

            _Instance = this;

            SyncRelics();

            The.Player?.RequirePart<UD_Player_RelicRevealer>();
        }

        #endregion

        public static bool IsRelic(GameObject Object, int ForReliquary = 0)
        {
            if (Object.GetPropertyOrTag($"{Utils.MOD_ID}.{nameof(RelicTrackerSystem)}.ExcludeRelic", $"{false}").EqualsNoCase($"{true}"))
                return false;

            if (Object.IsTemporary)
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
            SultanLoot sultanLoot = new();
            for (int i = 6; i > 0; i--)
            {
                sultanLoot.Period = i;
                if (NewRelicRecord(sultanLoot.generateFace(), ForReliquary: i) is RelicRecord maskRecord)
                    yield return maskRecord;

                foreach (string id in (HistoryAPI.GetSultanForPeriod(i)?.GetList("items")).IteratorSafe())
                {
                    if (The.Game.sultanHistory.GetEntitiesByDelegate(e => e.GetCurrentSnapshot().Name == id).FirstOrDefault() is not HistoricEntity relicEntity)
                        continue;

                    if (relicEntity.GetCurrentSnapshot() is not HistoricEntitySnapshot relicSnapshot)
                        continue;

                    if (RelicGenerator.GenerateRelic(relicSnapshot, RelicGenerator.GetRelicTierFromPeriod(int.Parse(relicSnapshot.GetProperty("period")))) is not GameObject relic)
                        continue;

                    if (NewRelicRecord(relic, ForReliquary: i) is not RelicRecord relicRecord)
                        continue;

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
                        Masks.Add(relicRecord);
                }
            }
        }

        protected IEnumerable<RelicRecord> GenerateOrderedRecords()
        {
            using var relics = ScopeDisposedList<RelicRecord>.GetFromPool();
            using var masks = ScopeDisposedList<RelicRecord>.GetFromPool();

            AddRecordsIfNone(GenerateZoneCacheRecords(), relics, masks);
            AddRecordsIfNone(GenerateReliquaryRecords(), relics, masks);
            AddRecordsIfNone(GenerateZoneRecords(), relics, masks);

            relics.StableSortInPlace(EraTierComparison);

            foreach (var maskRecord in masks.IteratorSafe())
            {
                if (!maskRecord.IsForReliquary)
                {
                    var relicsToRemove = relics.Where(r => r.IsMask && !r.IsDestroyed && r.ForReliquary == maskRecord.Era && r.RelicName == maskRecord.RelicName);
                    foreach (var relicToRemove in relicsToRemove)
                        relics.Remove(relicToRemove);
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
            if (_CachedRelicRecords.IsNullOrEmpty())
            {
                RegisterTrackerForSync(RelicTracker);
                return false;
            }

            if (!_CachedRelicRecords.TryGetValue(RelicTracker.TrackerID, out RelicRecord))
            {
                RegisterTrackerForSync(RelicTracker);
                return false;
            }

            RelicRecord.ParentTracker = RelicTracker;

            UnregisterTrackerForSync(RelicTracker);
            return true;
        }

        public void SyncRelics()
        {
            using var relicRecords = RentRecords();
            using var relicTrackers = ScopeDisposedList<UD_RelicTracker>.GetFromPoolFilledWith(_TrackersWantingRecords.Keys);

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
                            using (var recordsToRemove = RentRecords(r => r.IsMask && !r.IsDestroyed && r.ForReliquary == realMask.Era && r.RelicName == realMask.RelicName))
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

        public static async Task<UIUtils.CascadableResult> RevealRelicsAsync(
            RelicTrackerSystem RelicTrackerSystem,
            bool RethrowOnError = false,
            bool ForceNoRelics = false,
            bool Debug = false,
            string Context = null
            )
        {
            bool abilityContext = Context?.Contains("ability") is true;

            if (The.Game.GetIntGameState("RobberChimesTriggered") is int robberChimesTriggered
                && robberChimesTriggered > 1)
                RelicTrackerSystem.ProcessRobberChimesTriggered(TriggeredPeriod: robberChimesTriggered - 1);

            RelicTrackerSystem.ClearInvalid();

            using var relics = RelicTrackerSystem.RentRecords(IsEligibleToShow, EraTierComparison);

            int worldSeed = The.Game.GetWorldSeed();

            if (relics.IsNullOrEmpty()
                || ForceNoRelics)
            {
                await Popup.NewPopupMessageAsync(
                    message: $"There don't appear to be any relic records.\n\nThe world seed is {worldSeed.ToString().Color("c")}.{(ForceNoRelics && !relics.IsNullOrEmpty() ? "\n\n{{K|This is the result of the ForceNoRelics flag.}}" : null)}",
                    title: "{{R|No Relics}}",
                    afterRender: NoRelicsIcon
                );
                return UIUtils.CascadableResult.Continue;
            }

            var result = UIUtils.CascadableResult.BackSilent;

            using var options = new PickOptionDataSetAsync<RelicRecord, UIUtils.CascadableResult>();
            using var tB = TextBuilder.Get();
            try
            {
                string title = "{{W|Relic Tracker}}" + (Debug ? "{{W| ({{B|Debug Edition}})}}" : null);
                do
                {
                    tB.Clear()
                        .Append("Below are the relics that generated for this world.")
                        .AppendLine()
                        .AppendLine().Append("Select one to view it as though looking at it");
                    if (Debug)
                        tB.Append(", including its internals if the GameObject is still present");
                    tB.Append(".");

                    tB.AppendLine()
                        .AppendLine().Append($"The world seed is ").AppendColored("C", worldSeed.ToString()).Append(".");

                    if (abilityContext
                        && !Options.EnableShowOnWorldGen)
                        tB.AppendLine()
                            .AppendLine().AppendColored("K", "Note: there are options available in the options menu to show this popup at world gen and when loading a save.");

                    tB.AppendLineEnd();

                    options.Clear(Dispose: true);

                    foreach (var relic in relics)
                    {
                        options.Add(new PickOptionData<RelicRecord, Task<UIUtils.CascadableResult>>
                        {
                            Element = relic,
                            Text = !Debug ? relic.OptionDisplayString() : relic.DebugString(),
                            Hotkey = options.GetHotkeys().GetNextHotKey(),
                            Icon = relic.Render,
                            Callback = async e => await e.ViewRelicAsync(Debug)
                        });
                    }

                    result = await UIUtils.PerformPickOptionAsync(
                        OptionDataSet: options,
                        Title: title,
                        Intro: tB.ToString(),
                        IntroIcon: RelicsIcon,
                        NoBackButton: abilityContext,
                        CancelIsExit: true,
                        DefaultSelected: 0,
                        OnBackCallback: UIUtils.BackSilentResultAsync,
                        OnEscapeCallback: UIUtils.CancelSilentResultAsync);
                }
                while (result.IsContinue());
            }
            catch (Exception x)
            {
                Utils.Error($"{nameof(RevealRelicsAsync)} failed to get cached relics", x);
                if (RethrowOnError)
                    throw x;

                result = UIUtils.CascadableResult.Back;
            }

            return result;
        }

        private UIUtils.CascadableResult RevealRelics(
            ref WishParams WishParams,
            bool RethrowOnError = false,
            string Context = null
            )
            => RevealRelicsAsync(
                RelicTrackerSystem: this,
                RethrowOnError: RethrowOnError,
                ForceNoRelics: WishParams.ForceNoRelics,
                Debug: WishParams.Debug,
                Context: Context)
            .WaitResult()
            ;

        public UIUtils.CascadableResult RevealRelics(bool RethrowOnError = false, string Context = null)
        {
            WishParams wishParams = default;
            return RevealRelics(ref wishParams, RethrowOnError: RethrowOnError, Context: Context);
        }

        public static async Task<UIUtils.CascadableResult> RevealCherubimAsync(
            RelicTrackerSystem RelicTrackerSystem,
            bool RethrowOnError = false,
            bool ForceNoCherubim = false,
            bool Debug = false,
            string Context = null
            )
        {
            bool abilityContext = Context?.Contains("ability") is true;

            int worldSeed = The.Game.GetWorldSeed();

            if (RelicTrackerSystem.CherubRecords is not IEnumerable<CherubRecord> cherubRecords
                || cherubRecords.IsNullOrEmpty()
                || ForceNoCherubim)
            {
                await Popup.NewPopupMessageAsync(
                    message: $"There don't appear to be any cherubim records.\n\nThe world seed is {worldSeed.ToString().Color("c")}.{(ForceNoCherubim ? "\n\n{{K|This is the result of the ForceNoCherubim flag.}}" : null)}",
                    title: "{{R|No Cherubim}}",
                    afterRender: NoRelicsIcon
                );
                return UIUtils.CascadableResult.Continue;
            }

            var result = UIUtils.CascadableResult.BackSilent;

            using var options = new PickOptionDataSetAsync<CherubRecord, UIUtils.CascadableResult>();
            using var tB = TextBuilder.Get();
            try
            {
                string title = "{{W|Cherubim Viewer}}";
                do
                {
                    tB.Clear()
                        .Append("Below are the cherubim that generated for this world and which guard the tombs of its past sultans.")
                        .AppendLine()
                        .AppendLine().Append("Select one to view ");
                    if (!Debug)
                        tB.Append("it as though looking at it");
                    else
                        tB.Append("its GameObject \"Internals\"");
                    tB.Append(".");

                    tB.AppendLine()
                        .AppendLine().Append($"The world seed is ").AppendColored("C", worldSeed.ToString()).Append(".");

                    if (abilityContext
                        && !Options.EnableShowOnWorldGen)
                        tB.AppendLine()
                            .AppendLine().Append("{{K|Note: there are options available in the options menu to show this popup at world gen and when loading a save.}}");

                    tB.AppendLineEnd();

                    options.Clear(Dispose: true);

                    foreach (var record in RelicTrackerSystem.CherubRecords)
                    {
                        options.Add(new PickOptionData<CherubRecord, Task<UIUtils.CascadableResult>>
                        {
                            Element = record,
                            Text = !Debug ? record.ToString() : record.DebugString(),
                            Hotkey = options.GetHotkeys().GetNextHotKey(),
                            Icon = record.Cherub.RenderForUI("Look,Tooltip", AsIfKnown: true),
                            Callback = e => Task.Run(() =>
                            {
                                InventoryActionEvent.Check(
                                    Object: record.Cherub,
                                    Actor: The.Player,
                                    Item: record.Cherub,
                                    Command: !Debug ? "Look" : "ShowInternals");
                                return UIUtils.CascadableResult.Continue;
                            })
                        });
                    }

                    result = await UIUtils.PerformPickOptionAsync(
                        OptionDataSet: options,
                        Title: title,
                        Intro: tB.ToString(),
                        IntroIcon: CherubimIcon,
                        NoBackButton: abilityContext,
                        CancelIsExit: true,
                        DefaultSelected: 0,
                        OnBackCallback: UIUtils.BackSilentResultAsync,
                        OnEscapeCallback: UIUtils.CancelSilentResultAsync);
                }
                while (result.IsContinue());
            }
            catch (Exception x)
            {
                Utils.Error($"{nameof(RevealCherubimAsync)} failed to get cached cherubim", x);
                if (RethrowOnError)
                    throw x;

                result = UIUtils.CascadableResult.Back;
            }

            return result;
        }

        private UIUtils.CascadableResult RevealCherubim(
            ref WishParams WishParams,
            bool RethrowOnError = false,
            string Context = null
            )
            => RevealCherubimAsync(
                RelicTrackerSystem: this,
                RethrowOnError: RethrowOnError,
                ForceNoCherubim: WishParams.ForceNoCherubim,
                Debug: WishParams.Debug,
                Context: Context)
            .WaitResult()
            ;

        public UIUtils.CascadableResult RevealCherubim(bool RethrowOnError = false, string Context = null)
        {
            WishParams wishParams = default;
            return RevealCherubim(ref wishParams, RethrowOnError: RethrowOnError, Context: Context);
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

        private string GetProcessZoneProp(Type FromEvent)
            => FromEvent != null
            ? $"{nameof(RelicTrackerSystem)}.{FromEvent?.Name ?? "MISSING_EVENT"}"
            : $"{nameof(RelicTrackerSystem)}.{nameof(ProcessZone)}"
            ;

        private string GetProcessZoneProp(MinEvent FromEvent = null)
            => GetProcessZoneProp(FromEvent?.GetType())
            ;

        public bool ProcessZone(Zone Z, MinEvent FromEvent = null)
        {
            string zoneProp = GetProcessZoneProp(FromEvent);

            if (Z.GetZoneProperty(zoneProp).EqualsNoCase("true"))
                return true;

            Z.SetZoneProperty(zoneProp, "true");

            SultanLoot sultanLoot = null;
            using var clearedReliquaries = ScopeDisposedList<int>.GetFromPool();
            using var processedRelics = ScopeDisposedList<GameObject>.GetFromPool();
            foreach (var zoneObject in Z.YieldObjects())
            {
                foreach (var recursiveZoneObject in zoneObject.GetObjectsRecursively())
                {
                    if (recursiveZoneObject.InInventory is GameObject zoneObjectContainer)
                        if (sultanLoot == null
                            || !clearedReliquaries.Contains(sultanLoot.Period))
                            zoneObjectContainer.TryGetPart(out sultanLoot);

                    if (sultanLoot == null
                        || !clearedReliquaries.Contains(sultanLoot.Period))
                        recursiveZoneObject.TryGetPart(out sultanLoot);

                    if (sultanLoot != null
                        && !clearedReliquaries.Contains(sultanLoot.Period)
                        && sultanLoot.generated)
                    {
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
            Registrar.Register(ZoneActivatedEvent.ID, EventOrder.EXTREMELY_LATE);
            base.Register(Game, Registrar);
        }

        public override void RegisterPlayer(GameObject Player, IEventRegistrar Registrar)
        {
            Registrar.Register(BeforeTakeActionEvent.ID, EventOrder.EXTREMELY_LATE);
            base.RegisterPlayer(Player, Registrar);
        }

        public override bool HandleEvent(BeforeTakeActionEvent E)
        {
            try
            {
                if (!HasShown
                    && Options.EnableShowOnWorldGen)
                {
                    UD_Player_RelicRevealer.HasShown = true;
                    Instance.AskRevealWhat();
                }
            }
            finally
            {
                HasShown = true;
                // The.Player?.UnregisterEvent(this, BeforeTakeActionEvent.ID);
            }
            try
            {
                if (The.Player.CurrentZone is Zone z)
                {
                    if (ZoneWantsProcessing
                        && !z.GetZoneProperty(GetProcessZoneProp(typeof(AfterZoneActivatedEvent))).EqualsNoCase("true"))
                    {
                        z.SetZoneProperty(GetProcessZoneProp(typeof(AfterZoneActivatedEvent)), "true");
                        ProcessZone(z);
                    }
                }
            }
            finally
            {
                ZoneWantsProcessing = false;
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
            if (!E.Zone.GetZoneProperty(GetProcessZoneProp(typeof(AfterZoneActivatedEvent))).EqualsNoCase("true"))
            {
                ZoneWantsProcessing = true;
            }
            return base.HandleEvent(E);
        }

        private async Task<UIUtils.CascadableResult> AskRevealWhatAsync(
            bool RethrowOnError = false,
            bool ForceNoRelics = false,
            bool ForceNoCherubim = false,
            bool Debug = false,
            string Context = null
            )
        {
            var result = UIUtils.CascadableResult.Continue;

            int worldSeed = The.Game.GetWorldSeed();
            using var tB = TextBuilder.Get();
            using var options = new PickOptionDataSetAsync<RelicTrackerSystem, UIUtils.CascadableResult>();
            try
            {
                string title = $"Relic Revealer".Colored("yellow");
                do
                {
                    tB.Clear();
                    options.Clear(Dispose: true);

                    tB.AppendColored("Y", "Welcome to the Relic Tracker (and Cherubim Viewer)!")
                        .AppendLine()
                        .AppendLine().Append($"The world seed is ").AppendColored("C", worldSeed.ToString()).Append(".")
                        .AppendLine()
                        .AppendLine().AppendQuote("{{Y|Track Relics}}").Append(" allows you to view the sultan relics and masks that have generated for this world, ")
                            .Append("including those found in the sultan's reliquary in their tomb.")
                        .AppendLine()
                        .AppendLine().Append("The tracker will keep tabs on whether or not you've ").AppendColored("G", "claimed").Append(" a given relic, who was ")
                            .AppendColored("W", "last to hold it").Append(" if it's not in your possession, and whether the relic has been irrevocably ").AppendColored("r", "destroyed.")
                        .AppendLine()
                        .AppendLine().AppendQuote("{{Y|View Cherubim}}").Append(" displays a list of each of the type of cherubim that will be found guarding each of the sultan tombs. ")
                            .Append("Selecting one will let you view it as though ").AppendColored("W", "l").Append("ooking at it, ")
                            .Append("which will let you see the bonuses conferred to it by its ").AppendQuote("sultan theme").Append(".")
                        .AppendLineEnd();

                    options.Add(new()
                    {
                        Element = this,
                        Text = "Track Relics".Color("Y"),
                        Icon = ViewableRelicRecords.Select(r => r.Render).GetRandomElementCosmetic(),
                        Hotkey = 'r',
                        Callback = async e => (await RevealRelicsAsync(
                            RelicTrackerSystem: e,
                            RethrowOnError: RethrowOnError,
                            ForceNoRelics: ForceNoRelics,
                            Debug: Debug,
                            Context: Context)).ContinueIfNotCancel(),
                    });
                    options.Add(new()
                    {
                        Element = this,
                        Text = "View Cherubim".Color("Y"),
                        Icon = CherubRecords.GetRandomElementCosmetic().GetRenderable(),
                        Hotkey = 'c',
                        Callback = async e =>(await RevealCherubimAsync(
                            RelicTrackerSystem: e,
                            RethrowOnError: RethrowOnError,
                            ForceNoCherubim: ForceNoCherubim,
                            Debug: Debug,
                            Context: Context)).ContinueIfNotCancel(),
                    });


                    bool wishContext = Context?.Contains("wish") is true;

                    if (Options.EnableRelicRevealerActivatedAbility)
                    {
                        options.Add(new()
                        {
                            Element = this,
                            Text = "I don't want the above options as activated abilities anymore".Color("y"),
                            Icon = MissingIcon,
                            Callback = e => Task.Run(() =>
                            {
                                Options.EnableRelicRevealerActivatedAbility = false;
                                return UIUtils.CascadableResult.Continue;
                            }),

                        });
                    }
                    else
                    {
                        options.Add(new()
                        {
                            Element = this,
                            Text = "I want the above options as activated abilities".Color("y"),
                            Icon = MissingIcon,
                            Callback = e => Task.Run(() =>
                            {
                                Options.EnableRelicRevealerActivatedAbility = true;
                                return UIUtils.CascadableResult.Continue;
                            }),

                        });
                    }

                    if (!HasShown
                        || wishContext)
                    {
                        if (Options.EnableShowOnWorldGen)
                        {
                            options.Add(new()
                            {
                                Element = this,
                                Text = "Don't show this after world gen anymore".Color("y"),
                                Icon = MissingIcon,
                                Callback = e => Task.Run(() =>
                                {
                                    Options.EnableShowOnWorldGen = false;
                                    return UIUtils.CascadableResult.Continue;
                                }),

                            });
                        }
                        else
                        {
                            options.Add(new()
                            {
                                Element = this,
                                Text = "Always show this after world gen".Color("y"),
                                Icon = MissingIcon,
                                Callback = e => Task.Run(() =>
                                {
                                    Options.EnableShowOnWorldGen = true;
                                    return UIUtils.CascadableResult.Continue;
                                }),

                            });
                        }
                    }

                    if (HasShown
                        || wishContext)
                    {

                        if (Options.EnableReshowOnGameLoad)
                        {
                            options.Add(new()
                            {
                                Element = this,
                                Text = "Don't show this after loading a save anymore".Color("y"),
                                Icon = MissingIcon,
                                Callback = e => Task.Run(() =>
                                {
                                    Options.EnableReshowOnGameLoad = false;
                                    return UIUtils.CascadableResult.Continue;
                                }),

                            });
                        }
                        else
                        {
                            options.Add(new()
                            {
                                Element = this,
                                Text = "Always show this after loading a save".Color("y"),
                                Icon = MissingIcon,
                                Callback = e => Task.Run(() =>
                                {
                                    Options.EnableShowOnWorldGen = true;
                                    Options.EnableReshowOnGameLoad = true;
                                    return UIUtils.CascadableResult.Continue;
                                }),

                            });
                        }
                    }

                    result = await UIUtils.PerformPickOptionAsync(
                        OptionDataSet: options,
                        Title: title,
                        Intro: tB.ToString(),
                        IntroIcon: RevealerIcon,
                        NoBackButton: true,
                        CancelIsExit: true,
                        OnEscapeCallback: UIUtils.CancelSilentResultAsync);
                }
                while (result.IsContinue());

            }
            catch (Exception x)
            {
                Utils.Error($"Failed to ask player what they'd like to reveal", x);
                return UIUtils.CascadableResult.Cancel;
            }

            return result;
        }

        private UIUtils.CascadableResult AskRevealWhat(ref WishParams WishParams, bool RethrowOnError = false, string Context = null)
            => AskRevealWhatAsync(
                RethrowOnError: RethrowOnError,
                ForceNoRelics: WishParams.ForceNoRelics,
                ForceNoCherubim: WishParams.ForceNoCherubim,
                Debug: WishParams.Debug,
                Context: Context)
            .WaitResult()
            ;

        public UIUtils.CascadableResult AskRevealWhat(bool RethrowOnError = false, string Context = null)
        {
            WishParams wishParams = default;
            return AskRevealWhat(ref wishParams, RethrowOnError: RethrowOnError, Context: Context);
        }

        #region Wishes

        private ref struct WishParams
        {
            public bool ForceNoRelics;
            public bool ForceNoCherubim;
            public bool Debug;

            public WishParams(string Parameters)
            {
                bool none = Parameters?.Contains("none") is true;
                ForceNoRelics = none || Parameters?.Contains("no relics") is true;
                ForceNoCherubim = none || Parameters?.Contains("no cherubim") is true;
                Debug = Parameters?.Contains("debug") is true;
            }
        }

        [WishCommand(Command = "UD AskReveal")]
        public static bool AskReveal_WishHandler(string Parameters)
        {
            try
            {
                var wishParams = new WishParams(Parameters);
                var result = Instance.AskRevealWhat(ref wishParams, RethrowOnError: true, Context: "wish");
                return result.IsContinue()
                    || result.IsSilent();
            }
            catch
            {
                return false;
            }
        }

        [WishCommand(Command = "UD AskReveal")]
        public static bool AskReveal_WishHandler()
            => AskReveal_WishHandler(null)
            ;

        [WishCommand(Command = "UD RevealRelics")]
        public static bool RelicReveal_WishHandler(string Parameters)
        {
            try
            {
                var wishParams = new WishParams(Parameters);
                var result = Instance.RevealRelics(ref wishParams, RethrowOnError: true, Context: "wish");
                return result.IsContinue()
                    || result.IsSilent();
            }
            catch
            {
                return false;
            }
        }

        [WishCommand(Command = "UD RevealRelics")]
        public static bool RelicReveal_WishHandler()
            => RelicReveal_WishHandler(null)
            ;

        [WishCommand(Command = "UD RevealCherubim")]
        [WishCommand(Command = "UD RevealCherubs")]
        public static bool RevealCherubs_WishHandler(string Parameters)
        {
            try
            {
                var wishParams = new WishParams(Parameters);
                var result = Instance.RevealCherubim(ref wishParams, RethrowOnError: true, Context: "wish");
                return result.IsContinue()
                    || result.IsSilent();
            }
            catch
            {
                return false;
            }
        }

        [WishCommand(Command = "UD RevealCherubim")]
        [WishCommand(Command = "UD RevealCherubs")]
        public static bool RevealCherubs_WishHandler()
            => RevealCherubs_WishHandler(null)
            ;

        #endregion
    }
}
