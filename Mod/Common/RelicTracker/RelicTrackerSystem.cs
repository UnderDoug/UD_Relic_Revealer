using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using ConsoleLib.Console;

using Qud.UI;

using XRL;
using XRL.Collections;
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
    public class RelicTrackerSystem : IPlayerSystem, IPlayerMutator
    {
        private static RelicTrackerSystem _Instance;
        public static RelicTrackerSystem Instance
        {
            get
            {
                _Instance ??= The.Game?.GetSystem<RelicTrackerSystem>();
                _Instance.TrackRelics();
                return _Instance;
            }
            private set => _Instance = value;
        }

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

        public bool Initialized;

        private List<RelicRecord> _CachedRelicRecords;
        public IEnumerable<RelicRecord> CachedRelicRecords
        {
            get
            {
                if (_CachedRelicRecords == null)
                {
                    _CachedRelicRecords = new();
                    if (GetOrderedRelics() is IEnumerable<RelicRecord> orderedRecords)
                        _CachedRelicRecords.AddRange(orderedRecords);
                }

                if (!_CachedRelicRecords.IsNullOrEmpty())
                {
                    using var relicRecords = ScopeDisposedList<RelicRecord>.GetFromPoolFilledWith(_CachedRelicRecords);
                    foreach (var relicRecord in relicRecords)
                        if (!IsEligibleToShow(relicRecord))
                            RemoveRelic(relicRecord);
                }

                return _CachedRelicRecords;
            }
        }

        public bool HasShown;

        public RelicTrackerSystem()
        { }

        private static RelicTrackerSystem InitializeSystem() => new() { };

        [CallAfterGameLoaded]
        [GameBasedCacheInit]
        public static void RelicTrackerSystemInit()
        {
            Utils.Info($"{nameof(RelicTrackerSystem)}.{nameof(RelicTrackerSystemInit)} Called...");

            if (The.Game == null)
            {
                Utils.Info($"{nameof(The)}.{nameof(The.Game)} is null.");
                Instance = null;
                return;
            }

            _Instance ??= The.Game.GetSystem<RelicTrackerSystem>() ?? The.Game.RequireSystem(InitializeSystem);

            if (_Instance != null)
            {
                Utils.Info($"{nameof(Instance)} constructed and assigned!");
                // Loading.LoadTask($"Tracking Relics", Instance.Init, showToUser: false); // show to user once this does something (if it ever does)
            }
            else
                Utils.Error($"Failed to load {nameof(RelicTrackerSystem)}.");
        }

        public void Init()
        {
        }

        public void TrackRelics()
        {
            /*Utils.Log($"{nameof(RelicTrackerSystem)}.{nameof(TrackRelics)}, {nameof(Initialized)}: {Initialized}");*/
            if (!Initialized)
            {
                _CachedRelicRecords = null;
                Initialized = !CachedRelicRecords.IsNullOrEmpty();
                /*if (Initialized)
                    Utils.Log($"  Initialization Succeeded...");
                else
                    Utils.Log($"  Initialization Failed...");

                CachedRelicRecords.Loggregate(
                    Proc: RelicRecord.DebugString,
                    Empty: "no records",
                    PostProc: s => $"    : {s}");*/
            }
        }

        public void mutate(GameObject player)
        {
            Loading.LoadTask($"Tracking Relics", Instance.TrackRelics);
        }

        #region Serialization

        public sealed override bool WantFieldReflection => false;

        public override void Write(SerializationWriter Writer)
        {
            Writer.WriteNamedFields(this, GetType());

            Writer.Write(_CachedRelicRecords);
        }

        public override void Read(SerializationReader Reader)
        {
            Reader.ReadNamedFields(this, GetType());
            _CachedRelicRecords = Reader.ReadCompositeList<RelicRecord>();
        }

        public override void AfterLoad(XRLGame game)
        {
            base.AfterLoad(game);
            SyncRelics();
        }

        #endregion

        public IEnumerable<RelicRecord> GetOrderedRelics(IEnumerable<GameObject> Source)
        {
            foreach (var cachedObject in Source.IteratorSafe())
            {
                if (!cachedObject.HasStringProperty("RelicName"))
                    continue;

                if (!cachedObject.TryGetPart(out TakenAchievement takenAch)
                    || takenAch.AchievementID != Achievement.RECOVER_RELIC?.ID)
                    continue;

                yield return new RelicRecord(cachedObject);
            }
        }

        public IEnumerable<RelicRecord> GetCacheRelics()
        {
            foreach (var relicRecord in GetOrderedRelics(The.ZoneManager?.CachedObjects?.Values))
                yield return relicRecord;
        }

        public IEnumerable<RelicRecord> GetZoneRelics()
        {
            foreach (var zoneObject in (The.ActiveZone?.YieldObjects()).IteratorSafe())
                foreach (var relicRecord in GetOrderedRelics(zoneObject.GetObjectsRecursively()))
                    yield return relicRecord;
        }

        public IEnumerable<RelicRecord> GetOrderedRelics()
        {
            using var relics = ScopeDisposedList<RelicRecord>.GetFromPool();
            foreach (var relicRecord in GetCacheRelics())
                if (relics.None(r => r.Relic == relicRecord.Relic))
                    relics.Add(relicRecord);

            foreach (var relicRecord in GetZoneRelics())
                if (relics.None(r => r.Relic == relicRecord.Relic))
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

        public bool SyncRelicRecord(RelicRecord RelicRecord)
        {
            if (CachedRelicRecords is not IEnumerable<RelicRecord> relicRecords)
                return false;

            if (relicRecords?.FirstOrDefault(r => r.SameAs(RelicRecord)) is RelicRecord existingRecord)
            {
                if (existingRecord == RelicRecord)
                    return true;

                existingRecord.Unpin();
                RemoveRelic(existingRecord);
                existingRecord.Dispose();
            }

            _CachedRelicRecords.Add(RelicRecord);
            return true;
        }

        public bool SyncRelic(UD_RelicTracker RelicTracker)
        {
            if (CachedRelicRecords is not IEnumerable<RelicRecord> relicRecords)
                return false;

            if (RelicTracker.ParentObject is not GameObject relic)
                return false;

            if (relicRecords?.FirstOrDefault(r => r.SameAs(relic)) is RelicRecord existingRecord)
            {
                if (existingRecord == RelicTracker.RelicRecord)
                    return true;

                RelicTracker.RelicRecord?.Dispose();
                RelicTracker.RelicRecord = existingRecord;
                RelicTracker.RelicRecord.Unpin(RefreshRelic: true);
                return true;
            }

            return SyncRelicRecord(RelicTracker.RelicRecord);
        }

        public void SyncRelics()
        {
            using var relicRecords = ScopeDisposedList<RelicRecord>.GetFromPoolFilledWith(CachedRelicRecords);
            foreach (var relicRecord in relicRecords)
                if (relicRecord.Relic?.GetPart<UD_RelicTracker>() is UD_RelicTracker relicTracker)
                    SyncRelic(relicTracker);

            _CachedRelicRecords?.StableSortInPlace(TierComparison);
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

        public RelicRecord RecordRelic(GameObject Relic, RelicRecord SourceRecord = null)
        {
            if (FindRecordFor(Relic) != null)
                return null;

            _CachedRelicRecords ??= new();

            var record = new RelicRecord(Relic, SourceRecord);
            _CachedRelicRecords.Add(record);
            _CachedRelicRecords.StableSortInPlace(TierComparison);
            return record;
        }

        public bool TryRecordRelic(GameObject Relic, out RelicRecord RelicRecord)
            => (RelicRecord = RecordRelic(Relic)) != null
            ;

        public bool TryRecordDuplicateRelic(GameObject Relic, RelicRecord SourceRecord, out RelicRecord RelicRecord)
            => (RelicRecord = RecordRelic(Relic, SourceRecord)) != null
            ;

        public bool RemoveRelic(RelicRecord RelicRecord)
            => RelicRecord?.IsPinned() is not true
            && _CachedRelicRecords?.Remove(RelicRecord) is true
            ;

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

        public override void Register(XRLGame Game, IEventRegistrar Registrar)
        {
            base.Register(Game, Registrar);
        }

        public override void RegisterPlayer(GameObject Player, IEventRegistrar Registrar)
        {
            Registrar.Register(BeforeTakeActionEvent.ID, EventOrder.EXTREMELY_LATE);
            Registrar.Register(ZoneBuiltEvent.ID, EventOrder.EXTREMELY_LATE);
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
