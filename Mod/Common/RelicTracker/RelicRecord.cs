using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using ConsoleLib.Console;

using HistoryKit;

using Qud.API;
using Qud.UI;

using UD_Relic_Revealer.Mod.UI;

using XRL;
using XRL.Collections;
using XRL.Language;
using XRL.UI;
using XRL.World;
using XRL.World.Parts;
using XRL.World.Text;

using static UD_Relic_Revealer.Mod.Utils;
using static XRL.World.Parts.UD_RelicData;

namespace UD_Relic_Revealer.Mod
{
    [Serializable]
    public class RelicRecord : IComposite, IDisposable
    {
        public static string RelicEraProp => $"{MOD_ID}_{nameof(RelicRecord)}.{nameof(Era)}";
        public static string RelicItemTypeProp => $"{MOD_ID}_{nameof(RelicRecord)}.itemType";
        public static string RelicTypeProp => $"{MOD_ID}_{nameof(RelicRecord)}.Type";

        public static History SultanHistory => The.Game?.sultanHistory;

        protected Guid _TrackerID;
        public Guid TrackerID
        {
            get
            {
                if (_TrackerID.IsEmptyOrDefault())
                    TrackerID = Guid.NewGuid();
                return _TrackerID;
            }
            protected set
            {
                _TrackerID = value;
                if (ParentTracker?.RelicRecord == this)
                    ParentTracker.SetTrackerID(value);
            }
        }

        public int? _BaseID;
        public int BaseID
        {
            get => _BaseID ?? 0;
            protected set
            {
                _BaseID = value;
            }
        }

        [NonSerialized]
        public UD_RelicTracker ParentTracker;

        public GameObject Relic
        {
            get
            {
                if (!IsValid)
                    return null;

                if (!_Synched)
                    return ParentTracker?.ParentObject;

                if (ParentTracker == null
                    || ParentTracker.ParentObject == null
                    || ParentTracker.ParentObject.BaseID != BaseID
                    || ParentTracker.ParentObject.IsPooled())
                {
                    if (BaseID != 0
                        && GameObject.FindByID(BaseID) is GameObject foundObject)
                        Relic = foundObject;
                    else
                        Relic = null;
                }
                return ParentTracker?.ParentObject;
            }
            protected set
            {
                if (value != null)
                {
                    ClearCache();
                    BaseID = value.BaseID;
                    ParentTracker = value.RequirePart<UD_RelicTracker>().Init(this);
                    Pronouns = value?.GetPronounProvider();
                    _Synched = true;
                    _Valid = true;
                    Init();
                }
                else
                {
                    ParentTracker?.ParentObject?.RemovePart(ParentTracker);
                    ParentTracker = null;
                    if (!IsDestroyed
                        && ForReliquary <= 0
                        && !IsPinned())
                        _Valid = false;
                }
            }
        }

        private int? _Tier;
        public int Tier
        {
            get
            {
                if (_Tier == null)
                {
                    /*if ((Relic?.Blueprint).IsNullOrEmpty()
                        || PopulationManager.GetEach("BaseRelic_Book")?.Contains(Relic.Blueprint) is not true
                        || !Relic.TryGetPart(out Commerce commerce))
                        _Tier = Relic?.GetTier();
                    else
                        _Tier = (int)Math.Round((commerce.Value - 200) / 100.0);*/
                    _Tier = Relic?.GetTier()
                        ?? Relic?.GetPart<UD_RelicData>()?.Tier;
                }
                return _Tier.GetValueOrDefault();
            }
        }

        private string _Type;
        public string Type
        {
            get
            {
                if (_Type == null
                    && TryGetType(Relic, RelicName, out string type, Snapshot))
                    _Type = type;

                return _Type
                    ?? "Artifact";
            }
        }

        private int? _Era;
        public int Era
        {
            get
            {
                if (_Era == null
                    && TryGetEra(Relic, RelicName, out int era, Snapshot))
                    _Era = era;

                return _Era.GetValueOrDefault();
            }
        }

        [NonSerialized]
        public bool IsSultanRelic; // Added in 0.0.3

        private string _DisplayName;
        public string DisplayName => _DisplayName ??= GetRelicDisplayName(Relic);

        private string _DisplayNameShort;
        public string DisplayNameShort => _DisplayNameShort ??= GetRelicDisplayName(Relic, Short: true);

        private string _RelicName;
        public string RelicName => _RelicName ??= GetRelicRelicName(Relic);

        private string _Pronouns;
        public IPronounProvider Pronouns
        {
            get => Gender.GetIfExists(_Pronouns) as IPronounProvider
                ?? PronounSet.GetIfExists(_Pronouns) as IPronounProvider
                ?? Gender.GetIfExists("neuter")
                ?? PronounSet.GetIfExists("it/its") as IPronounProvider
                ;
            set => _Pronouns = value?.Name;
        }

        public string IndicativeProximal => Pronouns.CapitalizedIndicativeProximal;
        public string indicativeProximal => Pronouns.IndicativeProximal;
        public bool IsPlural => Pronouns.Plural;
        public string it => Pronouns.Subjective;
        public string @is => IsPlural ? "are" : "is";
        public string itIs => $"{it} {@is}";

        private string _DescriptiveNoun; // Added in 0.0.3
        public string DescriptiveNoun
        {
            get
            {
                if (_DescriptiveNoun.IsNullOrEmpty()
                    && TryGetNoun(Relic, RelicName, out string type, Snapshot))
                    _DescriptiveNoun = type;

                return _DescriptiveNoun
                    ?? Relic?.GetPropertyOrTag("CreatureType")
                    ?? "artifact"
                    ;
            }
        }

        public string DescriptiveNounArticle
            => DescriptiveNoun?.StartsWith("pair") is not false
            ? IsPlural.GetPlural(!Grammar.IndefiniteArticleShouldBeAn(DescriptiveNoun) ? "a" : "an", "some")
            : "a"
            ;

        private string _Subtype; // Added in 0.0.3
        public string Subtype
        {
            get
            {
                if (_Subtype.IsNullOrEmpty()
                    && TryGetSubtype(Relic, RelicName, out string subtype, Snapshot))
                    _Subtype = subtype;

                return _Subtype
                    ?? "artifact"
                    ;
            }
        }

        public string SubtypeDisplay
            => Subtype switch
            {
                "ranged" => $"weapon",
                "armor" => $"piece of {Subtype}",
                _ => Subtype,
            };

        private Renderable _Render;
        public IRenderable Render
        {
            get
            {
                if (_Render == null
                    && Relic?.RenderForUI("RelicRecord", AsIfKnown: true) is IRenderable render)
                    _Render ??= new Renderable(render);
                return _Render;
            }
        }

        private string _Description;
        public string Description
        {
            get
            {
                try
                {
                    if (_Description == null)
                    {
                        if (The.Player != null
                            || PopulationManager.GetEach("BaseRelic_Food")?.Contains(Relic?.Blueprint) is not true)
                            _Description = Relic?.GetPart<Description>()?.GetLongDescription();
                    }
                }
                catch (Exception x)
                {
                    WarnOnce($"Failed to {nameof(XRL.World.Parts.Description.GetLongDescription)} for {DebugString()}", x);
                }
                return _Description;
            }
        }

        private string _Story;
        public string Story => _Story ??= Relic?.GetPropertyOrTag("Story");

        private string _LastHeldBy;
        /// <summary>
        /// DisplayName of the last <see cref="Holder"/>, provided it is a valid <see cref="GameObject"/>, is not pooled, and that <see langword="this"/> is not pinned.
        /// </summary>
        public string LastHeldBy
        {
            get
            {
                if (Holder != null
                    && GameObject.Validate(Holder)
                    && !Holder.IsPooled()
                    && !IsPinned())
                {
                    _LastHeldBy = Holder.GetDisplayName(AsIfKnown: true, Single: true, Short: true, WithIndefiniteArticle: true, Reference: true);
                    LastHeldByPlayer = Holder.IsPlayer();
                }

                return _LastHeldBy;
            }
        }

        private bool _LastHeldByPlayer;
        /// <summary>
        /// Indicates whether the most recent valid <see cref="Holder"/> is <see cref="The.Player"/>.
        /// </summary>
        public bool LastHeldByPlayer
        {
            get => _LastHeldByPlayer;
            protected set
            {
                _LastHeldByPlayer = value;
                if (value)
                    IsClaimed = true;
            }
        }

        private bool? _IsMask;
        /// <summary>
        /// Indicates whether <see cref="Relic"/> is a sultan mask.
        /// </summary>
        public bool IsMask => _IsMask ??= (Relic?.HasPart(nameof(SultanMask)) is true);

        private int _ForReliquary;
        /// <summary>
        /// Indicates the sultan period reliquary <see cref="Relic"/> is intended for as loot. 
        /// </summary>
        public int ForReliquary
        {
            get => _ForReliquary;
            protected set => _ForReliquary = value;
        }

        /// <summary>
        /// Indicates whether <see cref="Relic"/> is intended as loot for a sultan reliquary.
        /// </summary>
        public bool IsForReliquary => ForReliquary > 0;

        private bool _IsClaimed;
        /// <summary>
        /// Indicates whether <see cref="Relic"/> has ever been in the possession of <see cref="The.Player"/>.
        /// </summary>
        public bool IsClaimed
        {
            get => _IsClaimed;
            protected set => _IsClaimed = value;
        }

        private bool _IsDestroyed;
        /// <summary>
        /// Indicates whether <see cref="Relic"/> has been destroyed in a way that should be tracked.
        /// </summary>
        public bool IsDestroyed
        {
            get => _IsDestroyed;
            set
            {
                _IsDestroyed = value;
                if (value)
                    Relic = null;
            }
        }

        /// <summary>
        /// Indicates whether <see cref="Relic"/> still exists in <see cref="The.ZoneManager.CachedObjects"/>.
        /// </summary>
        public bool IsCached
            => Relic != null
            && (The.ZoneManager?.CachedObjects?.Values).IteratorSafe().Any(go => go == Relic)
            ;

        /// <summary>
        /// Indicates whether <see cref="Relic"/> will be removed from <see cref="The.ZoneManager.CachedObjects"/> when the <see cref="Zone"/> it's intended for is built.
        /// </summary>
        public bool IsExitingCache
            => Relic == null
            || The.ZoneManager?.CachedObjectsToRemoveAfterZoneBuild?.Contains(Relic.ID) is true
            ;

        /// <summary>
        /// Indicates whether <see cref="Relic"/> is remaining cached or is displayable.
        /// </summary>
        public bool IsValidRecord
            => IsRemainingCached
            || HasDisplayableRelic
            ;

        /// <summary>
        /// Indicates whether <see cref="Relic"/> is currently cached, is not exiting cache, and this record is valid.
        /// </summary>
        public bool IsRemainingCached
            => IsCached
            && !IsExitingCache
            && IsValid
            ;

        /// <summary>
        /// Indicates whether <see cref="Relic"/> is a valid <see cref="GameObject"/>, that is not pooled, and has a non-default BaseID.
        /// </summary>
        public bool HasRealRelic
            => GameObject.Validate(Relic)
            && !Relic.IsPooled()
            && BaseID != 0
            ;

        /// <summary>
        /// Indicates whether this record is for a destroyed relic, a pinned relic, or a reliquary relic. All of these rely on cached display values, and <see cref="Relic"/> is likely <see langword="null"/>, pooled, or an invalid <see cref="GameObject"/>.
        /// </summary>
        public bool HasPseudoRelic
            => IsDestroyed
            || IsPinned()
            || ForReliquary > 0
            ;

        /// <summary>
        /// Indicates whether this record contains the minimum data necessary to display a mocked-up "look UI" for the relic it is intended to represent.
        /// </summary>
        public bool HasDisplayableRelic
            => (HasRealRelic
                || HasPseudoRelic)
            && Render != null
            && IsValid
            ;

        /// <summary>
        /// Flag to indicate whether this record is the one that <see cref="RelicTrackerSystem"/> has cached. Deserialization results in duplicate records which this flag assists in cleaning up.
        /// </summary>
        private bool _Synched;

        /// <summary>
        /// Flag to prevent most changes to this record, and to prevent it being removed from the <see cref="RelicTrackerSystem"/>.
        /// </summary>
        private bool _Pinned;

        /// <summary>
        /// Flag to manually indicate that this relic is no longer valid, should not be displayed, and will be imminently disposed. Can only be overridden by <see cref="_Pinned"/>.
        /// </summary>
        private bool _Valid = true;

        /// <summary>
        /// Indicates that this record is either flagged valid or is currently pinned.
        /// </summary>
        private bool IsValid
            => _Valid
            || IsPinned()
            ;

        /// <summary>
        /// The current holder of <see cref="Relic"/>.
        /// </summary>
        public GameObject Holder => Relic?.Holder;


        /// <summary>
        /// Indicates whether <see cref="Relic"/> is in the currently active zone.
        /// </summary>
        public bool IsInCurrentZone
            => The.ActiveZone != null
            && The.ActiveZone == (Relic?.CurrentZone ?? Relic?.InInventory?.CurrentZone)
            ;

        private IBookContents.BookPageInfo BookPageInfo; // Added in 0.0.3

        public HistoricEntitySnapshot Snapshot
            => SultanHistory
                ?.GetEntitiesByDelegate(e => e.GetCurrentSnapshot().Name == RelicName)
                ?.FirstOrDefault()
                ?.GetCurrentSnapshot()
            ;

        private List<ElementData> RelicElements = new(); // Added in 0.0.3
        private List<FactionFeelingData> RelicFactionFeelings = new(); // Added in 0.0.3

        public RelicRecord()
        { }

        public RelicRecord(GameObject Relic, int ForReliquary = 0)
            : this()
        {
            if (ForReliquary > 0)
                this.ForReliquary = ForReliquary;

            this.Relic = Relic;
        }

        public RelicRecord(GameObject Relic, RelicRecord SourceRecord, int IsForReliquary = 0)
            : this(Relic, IsForReliquary)
        {
            if (SourceRecord != null)
                IsClaimed = SourceRecord.IsClaimed;
        }

        public void Write(SerializationWriter Writer)
        {
            Writer.Write(TrackerID);

            Writer.Write(_BaseID.HasValue);
            Writer.WriteOptimized(BaseID);

            Writer.Write(_Tier.HasValue);
            Writer.WriteOptimized(Tier);

            Writer.Write(_Era.HasValue);
            Writer.WriteOptimized(Era);

            Writer.Write(IsSultanRelic);

            Writer.WriteOptimized(DisplayName);
            Writer.WriteOptimized(DisplayNameShort);
            Writer.WriteOptimized(RelicName);
            Writer.WriteOptimized(_Pronouns);

            Writer.WriteOptimized(DescriptiveNoun);
            Writer.WriteOptimized(Subtype);

            Writer.WriteComposite(_Render);
            Writer.WriteOptimized(Description);
            Writer.WriteOptimized(Story);
            Writer.WriteOptimized(LastHeldBy);
            Writer.Write(LastHeldByPlayer);
            Writer.WriteOptimized(ForReliquary);

            Writer.Write(_IsMask.HasValue);
            Writer.Write(IsMask);

            Writer.Write(IsClaimed);
            Writer.Write(IsDestroyed);
            Writer.Write(_Pinned);
            Writer.Write(_Valid);

            Writer.Write(BookPageInfo);

            Writer.WriteComposite(RelicElements);
            Writer.WriteComposite(RelicFactionFeelings);
        }

        public void Read(SerializationReader Reader)
        {
            _TrackerID = Reader.ReadGuid();

            if (Reader.ReadBoolean())
                _BaseID = Reader.ReadOptimizedInt32();
            else
                _ = Reader.ReadOptimizedInt32();

            if (Reader.ReadBoolean())
                _Tier = Reader.ReadOptimizedInt32();
            else
                _ = Reader.ReadOptimizedInt32();

            if (Reader.ReadBoolean())
                _Era = Reader.ReadOptimizedInt32();
            else
                _ = Reader.ReadOptimizedInt32();

            if (Reader.ModVersions.TryGetValue(MOD_ID, out XRL.Version readVersion))
            {
                if (readVersion < new XRL.Version(0, 0, 3))
                    IsSultanRelic = false;
                else
                    IsSultanRelic = Reader.ReadBoolean();
            }

            _DisplayName = Reader.ReadOptimizedString();
            _DisplayNameShort = Reader.ReadOptimizedString();
            _RelicName = Reader.ReadOptimizedString();
            _Pronouns = Reader.ReadOptimizedString();

            if (Reader.ModVersions.TryGetValue(MOD_ID, out readVersion))
            {
                if (readVersion < new XRL.Version(0, 0, 3))
                {
                    _DescriptiveNoun = "artifact";
                    _Subtype = "artifact";
                }
                else
                {
                    _DescriptiveNoun = Reader.ReadOptimizedString();
                    _Subtype = Reader.ReadOptimizedString();

                }
            }

            _Render = Reader.ReadComposite<Renderable>();
            _Description = Reader.ReadOptimizedString();
            _Story = Reader.ReadOptimizedString();
            _LastHeldBy = Reader.ReadOptimizedString();
            _LastHeldByPlayer = Reader.ReadBoolean();
            _ForReliquary = Reader.ReadOptimizedInt32();

            if (Reader.ReadBoolean())
                _IsMask = Reader.ReadBoolean();
            else
                _ = Reader.ReadBoolean();

            _IsClaimed = Reader.ReadBoolean();
            _IsDestroyed = Reader.ReadBoolean();
            _Pinned = Reader.ReadBoolean();
            _Valid = Reader.ReadBoolean();

            if (Reader.ModVersions.TryGetValue(MOD_ID, out readVersion))
            {
                if (readVersion >= new XRL.Version(0, 0, 3))
                {
                    BookPageInfo = Reader.ReadComposite() as IBookContents.BookPageInfo;

                    RelicElements = Reader.ReadCompositeList<ElementData>();
                    RelicFactionFeelings = Reader.ReadCompositeList<FactionFeelingData>();
                }
            }
        }

        public static string GetRelicDisplayName(GameObject Relic, bool Short = false)
            => Relic?.GetDisplayName(AsIfKnown: true, Short: Short, Reference: true)
            ;

        public static string GetRelicRelicName(GameObject Relic)
            => Relic?.GetPropertyOrTag(nameof(RelicName))
            ?? Relic?.Render?.DisplayName
            ;

        public bool SameAs(RelicRecord Other)
        {
            if (Other == null)
                return false;

            if (BaseID == Other.BaseID)
                return true;
            
            return (ForReliquary > 0
                    || Other.ForReliquary > 0)
                && ForReliquary == Other.ForReliquary
                && RelicName == Other.RelicName
                ;
        }

        public bool SameAs(GameObject Relic)
        {
            if (Relic == null)
                return false;

            if (BaseID == Relic.BaseID)
                return true;

            return ForReliquary > 0
                && RelicName == GetRelicRelicName(Relic);
                ;
        }

        public void SetClaimed()
            => IsClaimed = true
            ;

        public void Destroy()
        {
            var relic = Relic;

            IsDestroyed = true;

            if (relic != null
                && ForReliquary > 0)
                relic.Release();
        }

        public void Pin()
        {
            _Pinned = true;
        }

        public void Unpin(bool RefreshRelic = false)
        {
            _Pinned = false;
            if (RefreshRelic
                && Relic != null)
                RefreshCache();
        }

        public bool IsPinned()
        {
            return _Pinned;
        }

        public void ClearCache(bool Force = false)
        {
            if (!IsPinned())
            {
                if (Force
                    || !IsDestroyed)
                {
                    _Tier = null;
                    _Era = null;
                    _DisplayName = null;
                    _DisplayNameShort = null;
                    _Pronouns = null;
                    _DescriptiveNoun = null;
                    _Subtype = null;
                    _Render = null;
                    _Description = null;
                    _Story = null;

                    _LastHeldBy = null;
                    _LastHeldByPlayer = false;
                }
            }
        }

        public void RefreshCache(bool Force = false)
        {
            if (!IsPinned())
            {
                ClearCache(Force);
                if (Force
                    || !IsDestroyed)
                    Init();
            }
        }

        public RelicRecord Init()
        {
            Relic.SuspendExaminerDuringAction(delegate ()
            {
                _ = Tier;
                _ = Era;
                _ = DisplayName;
                _ = DisplayNameShort;
                _ = Pronouns;
                _ = DescriptiveNoun;
                _ = Subtype;
                _ = Render;
                _ = Description;
                _ = Story;

                _ = LastHeldBy;

                if (Relic != null
                    && Relic.TryGetPart(out UD_RelicData relicData))
                {
                    relicData.FinalizeData(Snapshot);

                    RelicElements ??= new();
                    RelicElements.Clear();
                    RelicElements.AddRange(relicData.YieldRelicElements());

                    RelicFactionFeelings ??= new();
                    RelicFactionFeelings.Clear();
                    RelicFactionFeelings.AddRange(relicData.YieldRelicFactionFeelings());
                }
                else
                if (Snapshot != null)
                {
                    RelicElements.AddRange(GetRelicElements(Snapshot));
                    RelicFactionFeelings.AddRange(GetRelicFactionFeelings(Snapshot));
                }

                if (IsSultanRelic)
                    _ = GetBookPageInfo();
            });
            return this;
        }

        public bool SetSynched(RelicTrackerSystem RelicTrackerSystem)
        {
            if (RelicTrackerSystem == null)
                return false;

            return _Synched = RelicTrackerSystem.HasRelicRecord(this, ForSync: true);
        }

        public bool ProcessRobberChimesTriggered(GameObject TriggeredReliquary = null, int? TriggeredPeriod = null)
        {
            if (TriggeredReliquary?.Blueprint is string reliquaryBlueprint
                && !reliquaryBlueprint.IsNullOrEmpty()
                && int.TryParse(reliquaryBlueprint[^1].ToString(), out int triggeredPeriod))
                TriggeredPeriod ??= triggeredPeriod;

            if ((Holder is GameObject holder
                    && holder != TriggeredReliquary
                    && holder.Blueprint.StartsWith("SultanReliquary"))
                || (IsForReliquary
                    && ForReliquary != TriggeredPeriod))
            {
                try
                {
                    Destroy();
                    return IsDestroyed;
                }
                catch (Exception x)
                {
                    WarnOnce($"Failed to {nameof(ProcessRobberChimesTriggered)} for {DebugString()}", x);
                    return false;
                }
            }
            return true;
        }

        public static bool TryGetEra(GameObject Relic, string RelicName, out int Era, HistoricEntitySnapshot Snapshot = null)
        {
            Era = 0;

            if (Relic != null
                && Relic.TryGetPart(out UD_RelicData relicData))
            {
                Era = relicData.Period;
                return true;
            }

            if (Relic?.GetStringProperty(RelicEraProp) is string relicPeriodProp
                && int.TryParse(relicPeriodProp, out Era))
                return true;

            if (!RelicName.IsNullOrEmpty())
            {
                Snapshot ??= SultanHistory?.GetEntitiesByDelegate(e => e.GetCurrentSnapshot().Name == RelicName)?.FirstOrDefault()?.GetCurrentSnapshot();

                if (Snapshot?.GetProperty("period", null) is string relicPeriod
                    && int.TryParse(relicPeriod, out Era))
                    return true;
            }

            if (Relic != null
                && Relic.TryGetPart(out SultanMask sultanMask)
                && (Era = sultanMask.Period) > 0)
                return true;

            return false;
        }

        public static bool TryGetNoun(GameObject Relic, string RelicName, out string Noun, HistoricEntitySnapshot Snapshot = null)
        {
            Noun = null;


            if (Relic != null
                && Relic.TryGetPart(out UD_RelicData relicData)
                && relicData.ItemType != "unknown"
                && !relicData.ItemType.IsNullOrEmpty())
            {
                Noun = relicData.ItemType;
                return true;
            }

            if (Relic?.GetStringProperty(RelicItemTypeProp) is string relicTypeProp
                && relicTypeProp != "unknown")
            {
                Noun = relicTypeProp;
                return true;
            }

            if (!RelicName.IsNullOrEmpty())
            {
                Snapshot ??= SultanHistory?.GetEntitiesByDelegate(e => e.GetCurrentSnapshot().Name == RelicName)?.FirstOrDefault()?.GetCurrentSnapshot();

                if (Snapshot?.GetProperty("itemType", null) is string relicType
                    && relicType != "unknown")
                {
                    Noun = relicType;
                    return true;
                }
            }

            if (Relic != null
                && Relic.TryGetPart(out SultanMask sultanMask))
            {
                Noun = "sultan mask";
                return true;
            }

            return false;
        }

        public static bool TryGetType(GameObject Relic, string RelicName, out string Type, HistoricEntitySnapshot Snapshot = null)
        {
            Type = null;

            if (Relic != null
                && Relic.TryGetPart(out UD_RelicData relicData)
                && !relicData.Type.IsNullOrEmpty())
            {
                Type = relicData.Type;
                return true;
            }

            if (Relic?.GetStringProperty(RelicTypeProp) is string relicTypeProp
                && !relicTypeProp.IsNullOrEmpty())
            {
                Type = relicTypeProp;
                return true;
            }

            if (!RelicName.IsNullOrEmpty())
            {
                Snapshot ??= SultanHistory?.GetEntitiesByDelegate(e => e.GetCurrentSnapshot().Name == RelicName)?.FirstOrDefault()?.GetCurrentSnapshot();

                if (Snapshot?.GetProperty("itemType", null) is string relicType
                    && !relicType.IsNullOrEmpty()
                    && RelicGenerator.TypeMap.TryGetValue(relicType, out relicType))
                {
                    Type = relicType;
                    return true;
                }
            }

            if (Relic?.HasPart<SultanMask>() is true)
            {
                Type = nameof(SultanMask);
                return true;
            }

            return false;
        }

        public static bool TryGetSubtype(GameObject Relic, string RelicName, out string Subtype, HistoricEntitySnapshot Snapshot = null)
        {
            Subtype = null;

            if (Relic != null
                && Relic.TryGetPart(out UD_RelicData relicData))
            {
                if (!relicData.Subtype.IsNullOrEmpty())
                {
                    Subtype = relicData.Subtype;
                    return true;
                }
                if (!relicData.Type.IsNullOrEmpty())
                {
                    Subtype = RelicGenerator.GetSubtype(relicData.Type);
                    return true;
                }
            }

            if (Relic?.GetStringProperty(RelicTypeProp) is string relicTypeProp
                && !relicTypeProp.IsNullOrEmpty())
            {
                Subtype = RelicGenerator.GetSubtype(relicTypeProp);
                return true;
            }

            if (!RelicName.IsNullOrEmpty())
            {
                Snapshot ??= SultanHistory?.GetEntitiesByDelegate(e => e.GetCurrentSnapshot().Name == RelicName)?.FirstOrDefault()?.GetCurrentSnapshot();

                if (Snapshot?.GetProperty("itemType", null) is string relicType
                    && !relicType.IsNullOrEmpty()
                    && RelicGenerator.TypeMap.TryGetValue(relicType, out relicType))
                {
                    Subtype = RelicGenerator.GetSubtype(relicType);
                    return true;
                }
            }

            if (Relic?.HasPart<SultanMask>() is true)
            {
                Subtype = nameof(SultanMask);
                return true;
            }

            return false;
        }

        public static string GetEraColor(int Era)
        {
            string color = "R";
            if (GetSultanMaskBlueprintByPeriod(Era)?.GetRenderable() is Renderable maskRender)
                color = maskRender.GetForegroundColor().ToString();
            return color;
        }

        public string GetEraColor()
            => GetEraColor(Era)
            ;

        public string GetEraDisplayString(bool SkipInit = false)
        {
            if (!SkipInit)
                Init();

            string symbol = Era > 0
                ? Era.OrdinalSuffix()
                : " ? "
                ;

            return symbol.Colored(GetEraColor());
        }

        public string GetStatus(bool SkipInit = false)
        {
            if (!SkipInit)
                Init();

            string symbol = " ";
            string color = "K";
            if (IsClaimed)
            {
                symbol = TICK;
                color = !IsCached ? "G" : "C";
            }
            else
            if (!IsRemainingCached
                || IsInCurrentZone)
                symbol = CIRC;

            if (Holder?.IsPlayer() is not true)
                color = "g";

            if (!LastHeldByPlayer)
                color = "W";

            if (IsDestroyed)
            {
                color = "r";
                if (!IsClaimed)
                    symbol = CROSS;
            }

            if (ForReliquary > 0)
            {
                symbol = LINES;
                color = !IsDestroyed ? "c" : "r";
            }

            return symbol.Colored(color);
        }

        public static string OptionDisplayString(RelicRecord RelicRecord)
        {
            using var tB = TextBuilder.Get()
                .Append("[").Append(RelicRecord?.GetStatus() ?? "{{C|?}}").Append("]")
                .Append("[").AppendColored("", RelicRecord?.GetEraDisplayString() ?? "{{R|?}}").Append("]")
                .Append("[Tier ").Append(RelicRecord?.Tier ?? 0).Append("] ")
                .Append(RelicRecord.DisplayNameShort ?? "MISSING_RECORD");

            return tB.ToString();
        }

        public string OptionDisplayString()
            => OptionDisplayString(this)
            ;

        public static string DebugString(RelicRecord RelicRecord)
        {
            using var tB = TextBuilder.Get()
                .Append("[").Append(RelicRecord.BaseID).Append("] ").Append(RelicRecord.DisplayNameShort?.Strip() ?? "MISSING").Append("; ")
                .AppendPair(nameof(RelicName), RelicRecord.RelicName ?? "NO_RELIC_NAME").Append("; ")
                .AppendPair(nameof(ForReliquary), RelicRecord.ForReliquary).Append("; ")
                .AppendPair(nameof(IsValidRecord), RelicRecord.IsValidRecord).Append("; ")
                .AppendPair(nameof(IsRemainingCached), RelicRecord.IsRemainingCached).Append("; ")
                .AppendPair(nameof(IsDestroyed), RelicRecord.IsDestroyed).Append("; ")
                .AppendPair(nameof(IsInCurrentZone), RelicRecord.IsInCurrentZone).Append("; ")
                .AppendPair(nameof(_Valid), RelicRecord._Valid).Append("; ")
                .AppendPair(nameof(_Pinned), RelicRecord._Pinned);

            return tB.ToString();
        }

        public string DebugString()
            => DebugString(this)
            ;

        public string GetPageTitle()
            => IsValidRecord
                || IsPinned()
            ? DisplayNameShort.Colored("W")
            : null
            ;

        public string GetPageContents()
        {
            if (!IsValidRecord
                && !IsPinned())
                return null;

            string sultanName = HistoryAPI.GetSultanForPeriod(Era)?.entity?.Name;
  
            using var pageBuilder = TextBuilder.Get();

            bool isDescriptivePlural = DescriptiveNoun?.StartsWith("pair") is not true && IsPlural;
            string isAre = isDescriptivePlural ? "are" : "is";

            using var elementsList = ScopeDisposedList<string>.GetFromPool();
            if (RelicElements.IsNullOrEmpty())
                elementsList.Add("being quite mysterious");
            else
                elementsList.AddRange(RelicElements.Select(a => a.GetOneSpiceEntry()));

            using var factionFeelingList = ScopeDisposedList<string>.GetFromPool();
            if (RelicFactionFeelings.IsNullOrEmpty())
                factionFeelingList.Add("keep largely to themself");
            else
                factionFeelingList.AddRange(RelicFactionFeelings.Select(f => (string)f));

            string elementsAndList = Grammar.MakeAndList(elementsList);
            string factionFeelingAndList = Grammar.MakeAndList(factionFeelingList);

            bool doExtra = Type != null
                && Type != "Book"
                && Type != "Artifact"
                && Type != "Food"
                ;

            pageBuilder
                .Append(GetPageTitle())
                .AppendLine()
                .AppendLine().Append(IndicativeProximal).Append(" ").Append(IsPlural.GetPlural("relic")).Append(" ").Append(isAre).Append(" ")
                    .Append((isDescriptivePlural || DescriptiveNoun?.Equals("armor") is true).GetPlural("a", "some")).Append(" tier ").Append(Tier)
                    .Append(" ").Append(DescriptiveNoun).Append(", which ").Append(isDescriptivePlural.GetPlural("was", "were")).Append(" owned by ")
                    .AppendColored(GetEraColor(), sultanName).Append(", the ").Append(GetEraDisplayString(SkipInit: true)).Append(" era sultan.");

            if (doExtra)
            {
                pageBuilder
                    .AppendLine()
                    .AppendLine().AppendColored(GetEraColor(), sultanName).Append(" was known for ").Append(elementsAndList).Append(". Perhaps ")
                        .Append(GetPageTitle()).Append(" was in some ways representitive of that.")
                    .AppendLine()
                    .AppendLine().Append("They were also known to ").Append(factionFeelingAndList).Append(", which is made evident by depictions of them with ")
                        .Append(indicativeProximal).Append(" particular ").Append(IsPlural.GetPlural("relic")).Append(".");
            }

            if (!IsForReliquary)
            {
                pageBuilder
                    .AppendLine()
                    .AppendLine().Append(Pronouns.CapitalizedPossessiveAdjective).Append(" last known location is lost to the sands of time.");
            }
            else
            {
                pageBuilder
                    .AppendLine()
                    .AppendLine().Append("It is broadly understood that ").Append(Pronouns.Subjective).Append(" ").Append(IsPlural.GetPlural("has", "have")).Append(" been buried with ").Append(Pronouns.PossessiveAdjective).Append(" owner.");
            }

            pageBuilder
                .AppendLine()
                .AppendLine().AppendColored("k", "This is a WIP book that you probably shouldn't be able to find yet...");

            pageBuilder
                .AppendLine().AppendColored("W", "Debug")
                .AppendLine().Append(nameof(Type)).Append(": ").Append(Type)
                .AppendLine().Append(nameof(Subtype)).Append(": ").Append(SubtypeDisplay).Append(" (").Append(Subtype).Append(")");

            if (doExtra)
            {
                string relicElementsDebug = RelicElements.Aggregate((string)null, (a, n) => a + (!a.IsNullOrEmpty() ? ", " : null) + n.GetElement(Colored: true)) ?? "none";
                string relicFeelingsDebug = RelicFactionFeelings.Aggregate((string)null, (a, n) => a + (!a.IsNullOrEmpty() ? ", " : null) + n.DebugString()) ?? "none";

                pageBuilder
                    .AppendLine().Append(nameof(RelicElements)).Append(": ").Append(relicElementsDebug)
                    .AppendLine().Append(nameof(RelicFactionFeelings)).Append(": ").Append(relicFeelingsDebug);
            }

            return pageBuilder.ToString();
        }

        public IBookContents.BookPageInfo GetBookPageInfo()
            => IsValidRecord
            ? BookPageInfo ??= new IBookContents.BookPageInfo
            {
                Title = UD_RelicTrackerBook.Title,
                Text = GetPageContents(),
                Format = "Auto",
                Margins = "1,2,2,2",
            }
            : null
            ;

        public async Task<UIUtils.CascadableResult> ViewRelicAsync(bool Internals = false)
        {
            if (!IsValidRecord
                && !IsPinned())
                return UIUtils.CascadableResult.Continue;

            _ = LastHeldBy;

            bool currentlyPlayerHeld = Holder?.IsPlayer() is true;

            using var tBDesc = TextBuilder.Get(Description);
            using var elements = ScopeDisposedList<StringPair>.GetFromPool();

            if (IsClaimed)
                elements.Add(new("have", "been {{G|claimed by you}}"));

            if (IsCached
                && !IsExitingCache)
                elements.Add(new(null, "currently cached".Colored("C")));

            if (ForReliquary > 0)
            {
                elements.Add(new(null, $"{$"inside the reliquary of {HistoryAPI.GetSultanForPeriod(ForReliquary).entity.Name}".Colored("W")}, the {Grammar.Ordinal(ForReliquary).Colored(GetEraColor(Era))} era sultan"));
            }
            else
            {
                if (!currentlyPlayerHeld
                    && IsInCurrentZone)
                    elements.Add(new(null, "somewhere {{W|in this zone}}"));

                if (!LastHeldBy.IsNullOrEmpty())
                {
                    if (currentlyPlayerHeld)
                        elements.Add(new(null, "currently {{G|in your possession}}"));
                    else
                    {
                        string lastHeldBy = LastHeldByPlayer
                            ? $"{"you".Colored("g")}, although you {"don't currently".Colored("W")} possess {it}"
                            : LastHeldBy.Colored("W");

                        elements.Add(new("", $"{IsPlural.GetPlural("was", "were")} last held by {lastHeldBy}"));
                    }
                }
                else
                if (!IsCached
                    || IsExitingCache)
                    elements.Add(new(null, "in an {{r|unknown}} last locaiton"));

                if (elements.IsNullOrEmpty())
                    elements.Add(new("have", "become {{r|untethered from reality}}"));
            }

            tBDesc.AppendRules("-----")
                .AppendLine().Append("Sultan era: ").AppendColored("", GetEraDisplayString() ?? "{{R|?}}")
                .AppendLine().Append("Tier: ").AppendColored("C", Tier.ToString())
                .AppendLine().Append(IndicativeProximal).Append(" ").Append(IsPlural.GetPlural("relic")).Append(" ")
                .Append(Grammar.MakeAndList(
                    Words: elements.Aggregate(
                        seed: new List<string>(),
                        func: delegate (List<string> acc, StringPair next)
                        {
                            acc.Add(GetProcessedItem(next, true, elements, Relic, it, @is, itIs).Replace(",", "``"));
                            return acc;
                        })
                    ).Replace("``", ","))
                .Append(".");

            if (IsDestroyed)
                tBDesc.AppendLine()
                    .Append(IndicativeProximal).Append(" ").Append(IsPlural.GetPlural("relic")).Append(" ")
                    .Append(@is).Append(" ").AppendColored("r", "no more").Append("; ")
                    .Append(it).Append(" ").Append(IsPlural.GetPlural("has", "have")).Append(" been irrevocably lost.");

            if (ForReliquary > 0
                && !IsDestroyed)
                tBDesc.AppendLine().AppendLine()
                    .AppendColored("C", "Please note:").Append(" due to being generated when the reliquary is first loaded, ")
                    .Append(indicativeProximal).Append(" ").Append(IsPlural.GetPlural("relic")).Append(" may ")
                    .AppendColored("W", "vary slightly").Append(" compared to what is presented above.")
                    .AppendLine()
                    .AppendColored("K", "Care has been taken to reduce these variations as much as possible.");

            if (Internals
                && Relic != null)
                tBDesc
                    .AppendLine()
                    .AppendLine()
                    .Append(GetDebugInternalsEvent.GetFor(Relic));

            using var tBName = TextBuilder.Get()
                .Append(DisplayName)
                ;

            var buttons = new List<QudMenuItem>(PopupMessage.SingleButton);

            if (!Story.IsNullOrEmpty())
                buttons.Add(new QudMenuItem
                {
                    command = "Story",
                    hotkey = "S",
                    text = "Recall {{W|S}}tory"
                });

            if ((await Popup.NewPopupMessageAsync(
                    message: tBDesc.ToString(),
                    buttons: buttons,
                    contextTitle: tBName.ToString(),
                    contextRender: Render)
                ).command == "Story")
            {
                BookUI.ShowBookByID(Story);
            }
            return UIUtils.CascadableResult.Continue;
        }

        public IEnumerable<string> GetDebugLines(bool FieldsOnly = true)
        {
            yield return $"{nameof(ParentTracker)}: {(ParentTracker != null ? "not " : null)}null";
            yield return $"{nameof(ParentTracker)}.{nameof(ParentTracker.ParentObject)}: {ParentTracker?.ParentObject?.DebugName ?? "null"}";

            yield return $"{nameof(BaseID)}: {BaseID}";
            yield return $"{nameof(_Tier)}: {_Tier?.ToString() ?? "null"}";
            yield return $"{nameof(_Era)}: {_Era?.ToString() ?? "null"}";
            yield return $"{nameof(_DisplayNameShort)}: {_DisplayNameShort ?? "null"}";
            yield return $"{nameof(_DisplayName)}: {_DisplayName ?? "null"}";
            yield return $"{nameof(_RelicName)}: {_RelicName ?? "null"}";
            yield return $"{nameof(_Pronouns)}: {_Pronouns ?? "null"}";
            yield return $"{nameof(_DescriptiveNoun)}: {_DescriptiveNoun ?? "null"}";
            yield return $"{nameof(IsPlural)}: {IsPlural}";
            yield return $"{nameof(IndicativeProximal)}: {IndicativeProximal}";
            yield return $"{nameof(it)}: {it ?? "null"}";
            yield return $"{nameof(@is)}: {@is ?? "null"}";
            yield return $"{nameof(itIs)}: {itIs ?? "null"}";
            yield return $"{nameof(_Render)}: {(_Render != null ? _Render.getTile() : "null")}";
            yield return $"{nameof(_Description)}: {(_Description != null ? $"{_Description.Length.Things("character")} long" : "null")}";
            yield return $"{nameof(_Story)}: {_Story ?? "null"}";
            yield return $"{nameof(_LastHeldBy)}: {_LastHeldBy ?? "null"}";
            yield return $"{nameof(_LastHeldByPlayer)}: {_LastHeldByPlayer}";
            yield return $"{nameof(_ForReliquary)}: {_ForReliquary}";
            yield return $"{nameof(_IsMask)}: {_IsMask?.ToString() ?? "null"}";
            yield return $"{nameof(_IsClaimed)}: {_IsClaimed}";

            if (!FieldsOnly)
            {
                yield return $"{nameof(IsCached)}: {IsCached}";
                yield return $"{nameof(IsExitingCache)}: {IsExitingCache}";
            }

            yield return $"{nameof(_IsDestroyed)}: {_IsDestroyed}";
            yield return $"{nameof(_Valid)}: {_Valid}";
            yield return $"{nameof(_Pinned)}: {_Pinned}";
            yield return $"{nameof(_Synched)}: {_Synched}";

            yield return $"{nameof(BookPageInfo)}: {(BookPageInfo == null ? "null" : null)}";
            if (BookPageInfo != null)
            {
                yield return $"{1.Indent()}{nameof(BookPageInfo.Title)}: {BookPageInfo.Title ?? "null"}";
                yield return $"{1.Indent()}{nameof(BookPageInfo.Format)}: {BookPageInfo.Format ?? "null"}";
                yield return $"{1.Indent()}{nameof(BookPageInfo.Margins)}: {BookPageInfo.Margins ?? "null"}";
                yield return $"{1.Indent()}{nameof(BookPageInfo.Text)}: {BookPageInfo.Text ?? "null"}";
            }

            if (!FieldsOnly)
            {
                yield return $"{nameof(IsInCurrentZone)}: {IsInCurrentZone}";
                yield return $"{nameof(Holder)}: {Holder?.DebugName ?? "null"}";
            }
        }

        public void Dispose()
        {
            var trackerID = TrackerID;
            try
            {
                if (ForReliquary > 0)
                    Relic?.Release();

                Relic = null;
                TrackerID = Guid.Empty;

                _Tier = null;
                _Era = null;

                _DisplayName = null;
                _DisplayNameShort = null;
                _RelicName = null;
                _Pronouns = null;
                _DescriptiveNoun = null;

                _Render = null;
                _Description = null;
                _Story = null;

                _LastHeldBy = null;
                _LastHeldByPlayer = false;
                _ForReliquary = 0;

                _IsMask = null;
                _IsClaimed = false;
                _IsDestroyed = false;
                _Valid = false;
                _Pinned = false;
                _Synched = false;

                BookPageInfo = null;
            }
            catch (Exception x)
            {
                WarnOnce($"Issue disposing of {nameof(RelicRecord)} {{{trackerID}}}", x);
            }
        }
    }
}
