using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using ConsoleLib.Console;

using HistoryKit;

using Qud.API;
using Qud.UI;

using XRL;
using XRL.Collections;
using XRL.Language;
using XRL.UI;
using XRL.World;
using XRL.World.Parts;

using static UD_Relic_Revealer.Mod.Utils;

namespace UD_Relic_Revealer.Mod
{
    [HasGameBasedStaticCache]
    [Serializable]
    public class CherubRecord : IComposite, IDisposable, IEquatable<CherubRecord>, IComparable<CherubRecord>
    {
        [GameBasedStaticCache(CreateInstance = false)]
        public static bool Initialized;

        public enum CherubVariant
        {
            A,
            B,
        }

        public int Period;
        public string Faction;
        public string Element;
        public CherubVariant Variant;

        private int BaseID;

        private GameObject _Cherub;
        public GameObject Cherub
        {
            get
            {
                if (_Cherub == null)
                {
                    if (BaseID <= 0
                        || !The.ZoneManager.CachedObjects.TryGetValue(BaseID.ToString(), out var cherub))
                    {
                        cherub = GameObject.CreateUnmodified($"CherubimSpawn{Period}{Variant}");
                        if (cherub != null)
                            BaseID = int.Parse(The.ZoneManager.CacheObject(cherub, cacheTwiceOk: true, replaceIfAlreadyCached: true));

                        string statePrefix = $"cherubim";
                        if (Variant == CherubVariant.B)
                            statePrefix += ":";
                        statePrefix += $"{Period}{Variant}";

                        Faction = The.Game.GetObjectGameState($"{statePrefix}faction") as string;
                        Element = The.Game.GetObjectGameState($"{statePrefix}element") as string;
                    }
                    _Cherub = cherub;
                }
                return _Cherub;
            }
        }

        public bool IsValid => Cherub != null;

        public CherubRecord()
        { }

        public CherubRecord(int Period, CherubVariant Variant)
            : this()
        {
            this.Period = Period;
            this.Variant = Variant;
            _ = Cherub;
            if (!IsValid)
                WarnOnce($"Failed to generate and/or cache cherub for {nameof(Period)} {Period} and {nameof(Variant)} {Variant}");
        }

        public void Write(SerializationWriter Writer)
        {
            Writer.WriteOptimized(BaseID);
        }

        public void Read(SerializationReader Reader)
        {
            BaseID = Reader.ReadOptimizedInt32();
        }

        public static IEnumerable<KeyValuePair<int, CherubVariant>> GetCherubPeriodVariantPairs(
            int From = 1,
            int To = 6,
            CherubVariant? Variant = null
            )
        {
            if (From > To)
                yield break;

            for (int i = To; i >= From; i--)
            {
                if (Variant.HasValue)
                {
                    yield return new(i, Variant.Value);
                    continue;
                }

                yield return new(i, CherubVariant.A);
                yield return new(i, CherubVariant.B);
            }
        }

        public string PeriodString(bool Colored = true, bool WithVariant = false)
            => (!WithVariant
                ? Period.OrdinalSuffix()
                : $"{Period}{Variant}")
            .Colored(Colored ? GetPeriodColor() : null)
            ;

        public static string GetPeriodColor(int Period)
            => RelicRecord.GetEraColor(Period)
            ;

        public string GetPeriodColor()
            => GetPeriodColor(Period)
            ;

        public string GetElementWord()
            => Element.ToLower() switch
            {
                "jewels" => "jeweled",
                "scholarship" => "learned",
                "might" => "mighty",
                "chance" => "chaotic",
                "circuitry" => "electric",
                "travel" => "quickened",
                _ => Element,
            }
            ;

        public static string GetElementColor(string Element)
            => Element switch
            {
                "glass" => "K",
                "jewels" => "M",
                "stars" => "Y",
                "time" => "b",
                "salt" => "y",
                "ice" => "C",
                "scholarship" => "B",
                "might" => "r",
                "chance" => "m",
                "circuitry" => "W",
                "travel" => "g",
                _ => "R",
            }
            ;

        public string GetElementColor()
            => GetElementColor(Element)
            ;

        public string ElementString()
            => GetElementWord().Colored(GetElementColor())
            ;

        public string GetCherubBlueprint()
            => $"{GetMechanical()}{Faction} Cherub"
            ;

        public string GetMechanical()
            => Period >= 4
            ? "Mechanical "
            : null
            ;

        public string CherubDisplayName()
        {
            if (Faction.IsNullOrEmpty())
                return "MISSING_FACTION";

            string blueprint = GetCherubBlueprint();
            string output;
            try
            {
                output = GameObjectFactory.Factory?.GetBlueprintIfExists(blueprint)?.DisplayName()
                    ?? $"MISSING: {blueprint}";
            }
            catch (Exception x)
            {
                WarnOnce($"{nameof(CherubRecord)}.{nameof(CherubDisplayName)} failed to get blueprint ({blueprint}) from {nameof(GameObjectFactory)}", x);
                output = $"INVALID: {blueprint}";
            }
            return output;
        }

        public override string ToString()
            => $"[{PeriodString()}][{Variant}] {GetMechanical()?.ToLower()}{ElementString()} {CherubDisplayName().Replace("mechanical ", "")}"
            ;

        public string DebugString()
            => Cherub != null
            ? $"[{PeriodString()}][{Variant}] {Cherub.DebugName}"
            : $"{ToString()} (MISSING_CHERUB)"
            ;

        public int CompareTo(CherubRecord other)
        {
            if (other == null)
                return -1;

            if (Period.CompareTo(other.Period) is int periodComp
                && periodComp != 0)
                return -periodComp;

            return Variant.CompareTo(other.Variant);
        }

        public bool Equals(CherubRecord other)
        {
            if (other == null)
                return Period == 0
                    || Faction.IsNullOrEmpty()
                    || Element.IsNullOrEmpty()
                    ;

            return Period == other.Period
                && Faction == other.Faction
                && Element == other.Element
                ;
        }

        public override bool Equals(object obj)
            => obj is CherubRecord cherubRecordObj
            ? Equals(cherubRecordObj)
            : base.Equals(obj)
            ;

        public override int GetHashCode()
            => Period.GetHashCode()
            ^ (Faction?.GetHashCode() ?? 0)
            ^ (Element?.GetHashCode() ?? 0)
            ;

        public void Dispose()
        {
            Period = 0;
            Faction = null;
            Element = null;
            Variant = default;
            BaseID = 0;
            if (_Cherub != null)
            {
                The.ZoneManager.CachedObjects.Remove(_Cherub.BaseID.ToString());
                _Cherub?.Release();
                _Cherub = null;
            }
        }

        public static bool operator ==(CherubRecord x, CherubRecord y)
        {
            if (x is null
                || y is null)
                return (x is null) == (y is null);

            return x.Equals(y)
                ;
        }

        public static bool operator !=(CherubRecord x, CherubRecord y)
            => !(x == y)
            ;
    }
}
