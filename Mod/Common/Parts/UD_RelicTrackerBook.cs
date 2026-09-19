using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using HistoryKit;

using UD_Relic_Revealer.Mod;

using XRL.Collections;
using XRL.Language;
using XRL.Rules;
using XRL.World.Text;

using static XRL.World.Parts.UD_RelicData;

namespace XRL.World.Parts
{
    public class UD_RelicTrackerBook : IBookContents
    {
        

        [GameBasedStaticCache(CreateInstance = false)]
        public static List<BookPageInfo> BookContents;

        public static string Title
            => "Relics of Sultans Past".Colored("W")
            ;

        public override string GetTitle()
            => Title
            ;

        public static List<BookPageInfo> CacheContents(RelicTrackerSystem Instance)
            => (BookContents ??= Instance
                ?.GetCompleteBookPageInfos()
                ?.ToList())
            ?? "6d4".RollCached().Aggregate(
                seed: new List<BookPageInfo>(),
                func: (a, i) =>
                {
                    a.Add(new BookPageInfo
                    {
                        Title = TextFilters.Weird(Title).CorruptText(),
                        Text = TextFilters.Weird($"Something terrible has happened... not even hope can save us...").CorruptText(),
                        Format = "Auto",
                        Margins = "1,2,2,2",
                    });
                    return a;
                })
            ;

        public override List<BookPageInfo> GetContents()
            => CacheContents(RelicTrackerSystem.Instance)
            ;
    }
}
