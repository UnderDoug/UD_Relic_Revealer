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
using XRL.World.Capabilities;
using XRL.World.Parts;

namespace UD_Relic_Revealer.Mod
{
    [HasGameBasedStaticCache]
    [HasWishCommand]
    public static class Utils
    {
        public static IRenderable NoRelicsIcon = new Renderable(
            Tile: "Abilities/abil_berate.bmp",
            ColorString: $"&K",
            TileColor: $"&K",
            DetailColor: 'R');

        [GameBasedStaticCache(CreateInstance = false)]
        public static IEnumerable<RelicRecord> _CachedRelicRecords;
        public static IEnumerable<RelicRecord> CachedRelicRecords => _CachedRelicRecords ??= GetOrderedRelics();

        public static IEnumerable<RelicRecord> GetOrderedRelics()
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

        [WishCommand(Command = "UD revealrelics")]
        public static bool RelicReveal_WishHandler()
        {
            if (GetOrderedRelics() is not IEnumerable<RelicRecord> relicRecords
                || relicRecords.IsNullOrEmpty())
            {
                Popup.NewPopupMessageAsync(
                    message: "There don't appear to be any relics.",
                    buttons: PopupMessage.SingleButton,
                    contextTitle: "No Relics",
                    contextRender: NoRelicsIcon
                ).Wait();
                return true;
            }

            using var relics = ScopeDisposedList<RelicRecord>.GetFromPoolFilledWith(relicRecords);
            using var relicOptions = ScopeDisposedList<string>.GetFromPoolFilledWith(relics.Select(r => $"[Tier {r?.Tier ?? 0}] {r?.DisplayName ?? "MISSING_RECORD"}"));
            using var relicRenders = ScopeDisposedList<IRenderable>.GetFromPoolFilledWith(relics.Select(r => r.Render));
            using var relicHotkeys = ScopeDisposedList<char>.GetFromPool();
            foreach (var relic in relics)
                relicHotkeys.Add(relicHotkeys.GetNextHotKey());

            var icon = GameObjectFactory.Factory?.GetBlueprintIfExists("Telescopic Monocle")?.GetRenderable();
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
                        IntroIcon: icon,
                        AllowEscape: true,
                        PopupID: nameof(RelicReveal_WishHandler));

                    if (result >= 0)
                        relics[result].ViewRelic();
                }
                while (result >= 0);
            }
            catch (Exception x)
            {
                ModManager.GetMod().Error($"{nameof(RelicReveal_WishHandler)} failed to get cached relics: {x}");
                return false;
            }

            return true;
        }
    }
}
