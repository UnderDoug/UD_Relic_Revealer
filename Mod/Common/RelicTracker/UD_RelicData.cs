using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using HistoryKit;

using UD_Relic_Revealer.Mod;

using XRL.Collections;
using XRL.Rules;

namespace XRL.World.Parts
{
    [Serializable]
    public class UD_RelicData : IScribedPart
    {
        [Serializable]
        public class FactionFeelingData : IComposite
        {
            [Serializable]
            public enum FeelingType
            {
                Hate,
                Like,
                Love,
            }

            private string Faction;
            private FeelingType Feeling;

            public string DisplayFaction
                => Faction == "*"
                ? "everyone"
                : Factions.GetIfExists(Faction)?.GetFormattedName()
                    ?? Faction.ToLower()
                    ?? "themself"
                ;

            private string _DisplayFeeling;
            public string DisplayFeeling
            {
                get
                {
                    if (_DisplayFeeling.IsNullOrEmpty())
                    {
                        _DisplayFeeling = HistoricStringExpander.ExpandString(
                            input: $"<spice.commonPhrases.{GetSpiceFeeling()}.!random>",
                            entity: null,
                            history: The.Game.sultanHistory);

                        if (_DisplayFeeling == string.Empty)
                            _DisplayFeeling = $"really {Feeling.ToString().ToLower()}";
                    }
                    return _DisplayFeeling;
                }
            }

            public FactionFeelingData()
            { }

            public FactionFeelingData(string Faction, FeelingType Feeling)
                : this()
            {
                if (!Faction.IsNullOrEmpty())
                    this.Faction = Faction;

                this.Feeling = Feeling;
            }

            public void Write(SerializationWriter Writer)
            {
                Writer.WriteOptimized(Faction);
                Writer.WriteOptimized((int)Feeling);
                Writer.WriteOptimized(DisplayFeeling);
            }

            public void Read(SerializationReader Reader)
            {
                Faction = Reader.ReadOptimizedString();
                Feeling = (FeelingType)(Math.Abs(Reader.ReadOptimizedInt32()) % Enum.GetValues(typeof(FeelingType)).Length);
                _DisplayFeeling = Reader.ReadOptimizedString();
            }

            public string GetFaction()
                => Faction
                ?? "MISSING_FACTION"
                ;

            public string GetFeeling()
                => Feeling.ToString()
                ;

            public string DebugString()
                => $"[{GetFeeling()}:{GetFaction()}]"
                ;

            public static string GetSpiceFeeling(FeelingType Feeling)
                => Feeling > FeelingType.Hate
                ? "love"
                : "abhor"
                ;

            public string GetSpiceFeeling()
                => GetSpiceFeeling(Feeling)
                ;

            public override string ToString()
                => $"{DisplayFeeling} {DisplayFaction}"
                ;


            public static explicit operator string(FactionFeelingData Operand)
                => Operand?.ToString()
                ;
        }

        [Serializable]
        public class ElementData : IComposite
        {
            private string Element;

            private string _Practice;
            public string Practice
                => _Practice ??= HistoricStringExpander.ExpandString(
                        input: GetRandomSpiceElementFor("practices"),
                        entity: null,
                        history: The.Game.sultanHistory)
                    .Colored(CherubRecord.GetElementColor(Element, null))
                ;

            private string _Nouns;
            public string Nouns
                => _Nouns ??= HistoricStringExpander.ExpandString(
                        input: GetRandomSpiceElementFor("nounsPlural"),
                        entity: null,
                        history: The.Game.sultanHistory)
                    .Colored(CherubRecord.GetElementColor(Element, null))
                ;

            private string _Adjective;
            public string Adjective
                => _Adjective ??= HistoricStringExpander.ExpandString(
                        input: GetRandomSpiceElementFor("adjectives"),
                        entity: null,
                        history: The.Game.sultanHistory)
                    .Colored(CherubRecord.GetElementColor(Element, null))
                ;

            private string _Quality;
            public string Quality
                => _Quality ??= HistoricStringExpander.ExpandString(
                        input: GetRandomSpiceElementFor("quality"),
                        entity: null,
                        history: The.Game.sultanHistory)
                    .Colored(CherubRecord.GetElementColor(Element, null))
                ;

            public ElementData()
            { }

            public ElementData(string Element)
                : this()
            {
                this.Element = Element;
            }

            public void Write(SerializationWriter Writer)
            {
                Writer.WriteOptimized(Element);
                Writer.WriteOptimized(Practice);
                Writer.WriteOptimized(Nouns);
                Writer.WriteOptimized(Adjective);
                Writer.WriteOptimized(Quality);
            }

            public void Read(SerializationReader Reader)
            {
                Element = Reader.ReadOptimizedString();
                _Practice = Reader.ReadOptimizedString();
                _Nouns = Reader.ReadOptimizedString();
                _Adjective = Reader.ReadOptimizedString();
                _Quality = Reader.ReadOptimizedString();
            }

            public bool SameAs(string Element)
                => this.Element == Element
                ;

            public bool SameAs(ElementData Other)
                => Other != null
                && Element == Other.Element
                ;

            public static string GetRandomSpiceElementFor(string Element, string Thing = "!random")
                => $"<spice.elements.{(Element != "none" && !Element.IsNullOrEmpty() ? Element : "!random")}.{Thing}.!random>"
                ;

            public string GetRandomSpiceElementFor(string Thing = "!random")
                => GetRandomSpiceElementFor(Element, Thing)
                ;

            public ElementData Init()
            {
                _ = Practice;
                _ = Nouns;
                _ = Adjective;
                _ = Quality;

                return this;
            }

            public string GetElement(bool Colored = false)
                => Colored
                ? Element.Colored(CherubRecord.GetElementColor(Element))
                : Element
                ;

            public override string ToString()
                => GetOneSpiceEntry()
                ;

            public string GetOneSpiceEntry()
                => Stat.RandomCosmetic(0, 3) switch
                {
                    3 => $"their {Quality ?? $"{Element}-like qualities".Colored(CherubRecord.GetElementColor(Element, null))}",
                    2 => $"being {Adjective ?? $"{Element}-like".Colored(CherubRecord.GetElementColor(Element, null))}",
                    1 => $"their {Nouns ?? $"{Element}".Colored(CherubRecord.GetElementColor(Element, null))}",
                    _ => $"{Practice ?? $"practices involving {Element}".Colored(CherubRecord.GetElementColor(Element, null))}",
                }
                ;
        }

        public string Type;
        public int Tier;
        public int Period;

        public string ItemType;
        public string Subtype;

        public string RelicName;

        private List<ElementData> RelicElements = new();
        private List<FactionFeelingData> RelicFactionFeelings = new();

        public UD_RelicData()
            : base()
        { }

        public override void Write(GameObject Basis, SerializationWriter Writer)
        {
            base.Write(Basis, Writer);
            Writer.WriteComposite(RelicElements);
            Writer.WriteComposite(RelicFactionFeelings);
        }

        public override void Read(GameObject Basis, SerializationReader Reader)
        {
            base.Read(Basis, Reader);
            RelicElements = Reader.ReadCompositeList<ElementData>();
            RelicFactionFeelings = Reader.ReadCompositeList<FactionFeelingData>();
        }

        public static IEnumerable<ElementData> GetRelicElements(HistoricEntitySnapshot Snapshot)
        {
            using var properties = ScopeDisposedList<string>.GetFromPoolFilledWith((Snapshot?.properties?.Values).IteratorSafe());

            foreach (var value in (Snapshot?.listProperties?.Values).IteratorSafe())
                properties.AddRange(value);

            foreach (var property in properties)
                if (RelicGenerator.TranslateAdjective(property) is string adjective
                    && !adjective.IsNullOrEmpty()
                    && adjective != "none")
                    yield return new ElementData(adjective).Init();
        }

        public static IEnumerable<FactionFeelingData> GetRelicFactionFeelings(HistoricEntitySnapshot Snapshot)
        {
            foreach ((var property, var values) in (Snapshot?.listProperties).IteratorSafe())
            {
                if (property == "likedFactions")
                {
                    foreach (string value in values)
                        if (Factions.Exists(value))
                            yield return new FactionFeelingData(value, FactionFeelingData.FeelingType.Like);
                }
                else
                if (property == "lovedFactions")
                {
                    foreach (string value in values)
                        if (Factions.Exists(value))
                            yield return new FactionFeelingData(value, FactionFeelingData.FeelingType.Love);
                }
                else
                if (property == "hatedFactions")
                {
                    foreach (string value in values)
                        if (Factions.Exists(value))
                            yield return new FactionFeelingData(value, FactionFeelingData.FeelingType.Hate);
                }
            }
        }


        public IEnumerable<ElementData> YieldRelicElements()
        {
            foreach (var elementData in RelicElements.IteratorSafe())
                yield return elementData;
        }

        public IEnumerable<FactionFeelingData> YieldRelicFactionFeelings()
        {
            foreach (var factionFeelingData in RelicFactionFeelings.IteratorSafe())
                yield return factionFeelingData;
        }

        public override void Attach()
        {
            base.Attach();

            if (Type == "Book"
                && ParentObject.GetStat("Level") is Statistic level)
                level.BaseValue = Tier * 5;

            ParentObject.SetStringProperty(RelicRecord.RelicTypeProp, Type, RemoveIfNull: true);
        }

        public void FinalizeData(HistoricEntitySnapshot Snapshot)
        {
            if (RelicElements.IsNullOrEmpty())
            {
                RelicElements ??= new();
                if (Snapshot != null)
                    RelicElements.AddRange(GetRelicElements(Snapshot));
            }
            CompleteElements();

            if (Snapshot != null)
                RelicFactionFeelings.AddRange(GetRelicFactionFeelings(Snapshot));
        }

        public void CompleteElements()
        {
            if (ParentObject.TryGetPart(out ItemElements itemElements))
                foreach (var itemElement in (itemElements.ElementMap?.Keys).IteratorSafe())
                    if (RelicElements.None(e => e.SameAs(itemElement)))
                        RelicElements.Add(new ElementData(itemElement));
        }

        public override bool WantEvent(int ID, int Cascade)
            => base.WantEvent(ID, Cascade)
            || ID == AfterElementBestowalEvent.ID
            || ID == AfterBasicBestowalEvent.ID
            ;

        public override bool HandleEvent(AfterBasicBestowalEvent E)
        {
            if (!E.Type.IsNullOrEmpty())
                Type ??= Type;

            if (!E.Subtype.IsNullOrEmpty())
                Subtype ??= E.Subtype;

            Tier = Capabilities.Tier.Constrain(E.Tier);

            return base.HandleEvent(E);
        }

        public override bool HandleEvent(AfterElementBestowalEvent E)
        {
            if (E.Element != "none")
                RelicElements.Add(new ElementData(E.Element).Init());

            if (!E.Type.IsNullOrEmpty())
                Type ??= Type;

            if (!E.Subtype.IsNullOrEmpty())
                Subtype ??= E.Subtype;

            Tier = Capabilities.Tier.Constrain(E.Tier);

            return base.HandleEvent(E);
        }
    }
}
