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

namespace UD_Relic_Revealer.Mod.RelicTracker
{
    [HasModSensitiveStaticCache]
    [HasGameBasedStaticCache]
    [HasCallAfterGameLoaded]
    [HasWishCommand]
    [Serializable]
    public class RelicTracker : IPlayerSystem
    {
        [GameBasedStaticCache(CreateInstance = false)]
        private static RelicTracker _System;
        public static RelicTracker System
        {
            get
            {
                if (_System == null)
                    BonesManagerSystemInit();
                return _System;
            }
            private set => _System = value;
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

        [SerializeField]
        private string GameID;

        public bool Initialized;

        public List<RelicRecord> _CachedRelicRecords;
        public IEnumerable<RelicRecord> CachedRelicRecords => _CachedRelicRecords ??= new(GetOrderedRelics());

        public RelicTracker()
        { }

        private static RelicTracker InitializeSystem() => new() { GameID = The.Game?.GameID };

        [CallAfterGameLoaded]
        [GameBasedCacheInit]
        public static void BonesManagerSystemInit()
        {
            if (_System == null)
            {
                System = The.Game?.RequireSystem(InitializeSystem);
                if (_System != null
                    && _System.GameID == null)
                    _System.GameID = The.Game.GameID;
            }
            else
            if (_System.GameID != null
                && _System.GameID != The.Game?.GameID)
            {
                System = null;
                BonesManagerSystemInit();
                return;
            }
            else
            if (The.Game?.GetSystem<RelicTracker>() == null)
                The.Game?.AddSystem(System);

            if (_System != null)
                Loading.LoadTask($"Tracking Relics", System.TrackRelics, showToUser: false); // show to user once this does something (if it ever does)
            else
            if (The.Game != null)
                Utils.Error($"Failed to load {nameof(RelicTracker)}.");
        }

        public void TrackRelics()
        {
        }

        [ModSensitiveCacheInit]
        public static void AddAchievement()
        {
            if (AchievementManager.State?.Stats != null)
                if (!AchievementManager.State.Stats.ContainsKey("STAT_WEAR_FACE_7"))
                    StatInfo.Create("STAT_WEAR_FACE_7", 1);
        }

        #region Serialization

        public sealed override bool WantFieldReflection => false;

        public override void Write(SerializationWriter Writer)
        {
            Writer.WriteNamedFields(this, GetType());
        }

        public override void Read(SerializationReader Reader)
        {
            Reader.ReadNamedFields(this, GetType());
        }

        #endregion

        public IEnumerable<RelicRecord> GetOrderedRelics()
        {
            using var relics = ScopeDisposedList<RelicRecord>.GetFromPool();
            foreach (var cachedObject in (The.ZoneManager?.CachedObjects?.Values).IteratorSafe())
            {
                if (!cachedObject.HasStringProperty("RelicName"))
                    continue;

                if (!cachedObject.TryGetPart(out TakenAchievement takenAch)
                    || takenAch.AchievementID != Achievement.RECOVER_RELIC?.ID)
                    continue;

                relics.Add(new RelicRecord(cachedObject));
            }

            relics.StableSortInPlace(delegate (RelicRecord x, RelicRecord y)
            {
                if (x == null
                    || y == null)
                    return (x == null).CompareTo(y == null);

                if (x.Tier.CompareTo(y.Tier) is int tierComp
                    && tierComp != 0)
                    return tierComp;

                return (x?.DisplayName?.Strip()).CompareTo(y?.DisplayName?.Strip());
            });

            foreach (var relic in relics.IteratorSafe())
                yield return relic;
        }

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
            using var relicOptions = ScopeDisposedList<string>.GetFromPoolFilledWith(relics.Select(r => $"[Tier {r?.Tier ?? 0}] {r?.DisplayName ?? "MISSING_RECORD"}"));
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
                ModManager.GetMod().Error($"{nameof(RevealRelics)} failed to get cached relics: {x}");
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
            base.RegisterPlayer(Player, Registrar);
        }

        public override bool HandleEvent(BeforeTakeActionEvent E)
        {
            try
            {
                System.RevealRelics();
            }
            finally
            {
                The.Player.UnregisterEvent(this, BeforeTakeActionEvent.ID);
            }
            return base.HandleEvent(E);
        }

        #region Wishes

        [WishCommand(Command = "UD revealrelics")]
        public static bool RelicReveal_WishHandler()
        {
            try
            {
                System.RevealRelics(RethrowOnError: true);
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
