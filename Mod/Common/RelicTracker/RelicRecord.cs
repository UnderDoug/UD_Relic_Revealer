using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using ConsoleLib.Console;

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
    [Serializable]
    public class RelicRecord : IComposite, IDisposable
    {
        public int BaseID
        {
            get
            {
                if (RelicReference?.ID == 0)
                    RelicReference.ID = Relic?.BaseID ?? 0;
                return RelicReference?.ID ?? 0;
            }
        }

        private GameObjectReference RelicReference;

        public GameObject Relic
        {
            get
            {
                RelicReference ??= new();
                if (RelicReference.Object == null
                    || RelicReference.Object.BaseID != RelicReference.ID
                    || RelicReference.Object.IsPooled())
                {
                    if (RelicReference.ID != 0
                        && GameObject.FindByID(RelicReference.ID) is GameObject foundObject)
                        RelicReference.Set(foundObject);
                    else
                    {
                        GameObjectReference.Free(ref RelicReference);
                        if (!IsDestroyed
                            && ForReliquary <= 0)
                            RelicTrackerSystem.Instance?.RemoveRelic(this);
                    }
                }
                return RelicReference?.Object;
            }
            protected set => (RelicReference ??= new()).Set(value);
        }

        private int? _Tier;
        public int Tier => (_Tier ??= Relic?.GetTier()) ?? 0;

        private string _DisplayName;
        public string DisplayName => _DisplayName ??= GetRelicDisplayName(Relic);

        private string _RelicName;
        public string RelicName => _RelicName ??= Relic.GetPropertyOrTag(nameof(RelicName));

        private string _IndicativeProximal;
        public string IndicativeProximal => _IndicativeProximal ??= Relic?.IndicativeProximal;

        private bool? _IsPlural;
        public bool IsPlural => _IsPlural ??= (Relic?.IsPlural is true);

        private string _it;
        public string it => _it ??= Relic?.it ?? "it";

        private string _is;
        public string @is => _is ??= Relic?.Are() ?? "is";

        private string _itIs;
        public string itIs => _itIs ??= Relic?.itis ?? "it is";

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
                    _Description ??= Relic?.GetPart<Description>()?.GetLongDescription();
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

        private int _ForReliquary;
        public int ForReliquary
        {
            get => _ForReliquary;
            protected set => _ForReliquary = value;
        }

        private bool _IsClaimed;
        public bool IsClaimed
        {
            get => _IsClaimed;
            protected set => _IsClaimed = value;
        }

        public bool IsCached
            => Relic != null
            && (The.ZoneManager?.CachedObjects?.Values).IteratorSafe().Any(go => go == Relic)
            ;

        public bool IsExitingCache
            => Relic == null
            || The.ZoneManager?.CachedObjectsToRemoveAfterZoneBuild?.Contains(Relic.ID) is true
            ;

        public bool IsRemainingCached
            => IsCached
            && !IsExitingCache
            ;

        public bool HasValidRelic
            => (IsDestroyed
                || IsPinned()
                || (GameObject.Validate(Relic)
                    && !Relic.IsPooled()
                    && BaseID != 0)
                || ForReliquary > 0)
            && Render != null
            && (_Valid
                || IsPinned())
            ;

        private bool _IsDestroyed;
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

        private bool _Pinned;

        private bool _Valid = true;

        private bool _Synched;

        public GameObject Holder => Relic?.Holder;

        public RelicRecord()
        { }

        public RelicRecord(GameObject Relic, int IsForReliquary = 0)
            : this()
        {
            this.Relic = Relic;

            var trackerPart = this.Relic.RequirePart<UD_RelicTracker>();
            trackerPart.RelicRecord = this;

            if (IsForReliquary > 0)
                this.ForReliquary = IsForReliquary;

            Init();
        }

        public RelicRecord(GameObject Relic, RelicRecord SourceRecord, int IsForReliquary = 0)
            : this(Relic, IsForReliquary)
        {
            if (SourceRecord != null)
                IsClaimed = SourceRecord.IsClaimed;
        }

        public void Write(SerializationWriter Writer)
        {
            Writer.Write(RelicReference);
            Writer.WriteOptimized(Tier);
            Writer.WriteOptimized(DisplayName);
            Writer.WriteOptimized(RelicName);
            Writer.WriteOptimized(IndicativeProximal);
            Writer.Write(IsPlural);
            Writer.WriteOptimized(it);
            Writer.WriteOptimized(@is);
            Writer.WriteOptimized(itIs);
            Writer.WriteComposite(_Render);
            Writer.WriteOptimized(Description);
            Writer.WriteOptimized(Story);
            Writer.WriteOptimized(LastHeldBy);
            Writer.Write(LastHeldByPlayer);
            Writer.WriteOptimized(ForReliquary);
            Writer.Write(IsClaimed);
            Writer.Write(IsDestroyed);
            Writer.Write(_Valid);
            Writer.Write(_Pinned);
        }

        public void Read(SerializationReader Reader)
        {
            RelicReference = Reader.ReadGameObjectReference();
            _Tier = Reader.ReadOptimizedInt32();
            _DisplayName = Reader.ReadOptimizedString();
            _RelicName = Reader.ReadOptimizedString();
            _IndicativeProximal = Reader.ReadOptimizedString();
            _IsPlural = Reader.ReadBoolean();
            _it = Reader.ReadOptimizedString();
            _is = Reader.ReadOptimizedString();
            _itIs = Reader.ReadOptimizedString();
            _Render = Reader.ReadComposite<Renderable>();
            _Description = Reader.ReadOptimizedString();
            _Story = Reader.ReadOptimizedString();
            _LastHeldBy = Reader.ReadOptimizedString();
            _LastHeldByPlayer = Reader.ReadBoolean();
            _ForReliquary = Reader.ReadOptimizedInt32();
            _IsClaimed = Reader.ReadBoolean();
            _IsDestroyed = Reader.ReadBoolean();
            _Valid = Reader.ReadBoolean();
            _Pinned = Reader.ReadBoolean();
        }

        public static string GetRelicDisplayName(GameObject Relic)
            => Relic?.GetDisplayName(AsIfKnown: true, Reference: true)
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
                && RelicName == Relic?.GetPropertyOrTag("RelicName");
                ;
        }

        public void SetClaimed()
            => IsClaimed = true
            ;

        public void Destroy()
        {
            IsDestroyed = true;
        }

        public void Pin(bool ClearRelic = false)
        {
            _Pinned = true;
            if (ClearRelic
                && RelicReference != null)
                RelicReference.Object = null;
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
                    _DisplayName = null;
                    _IndicativeProximal = null;
                    _IsPlural = null;
                    _it = null;
                    _is = null;
                    _itIs = null;
                    _Render = null;
                    _Description = null;
                    _Story = null;
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

        public void Init()
        {
            Relic.SuspendExaminerDuringAction(delegate ()
            {
                _ = BaseID;
                _ = Tier;
                _ = DisplayName;
                _ = IndicativeProximal;
                _ = IsPlural;
                _ = it;
                _ = @is;
                _ = itIs;
                _ = Render;
                _ = Description;
                _ = Story;

                _ = LastHeldBy;
            });
        }

        public bool SetSynched(RelicTrackerSystem RelicTrackerSystem)
        {
            if (RelicTrackerSystem == null)
                return false;

            return _Synched = RelicTrackerSystem.HasRelicRecord(this, ForSync: true);
        }

        public string GetStatus()
        {
            Init();

            string symbol = " ";
            string color = "K";
            if (IsClaimed)
            {
                symbol = TICK;
                color = !IsCached ? "G" : "C";
            }
            else
            if (!IsRemainingCached)
                symbol = "-";

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
                symbol = $"{ForReliquary}";
                color = "K";
            }

            return symbol.Colored(color);
        }

        public static string OptionDisplayString(RelicRecord RelicRecord)
            => Event.NewStringBuilder()
                .Append("[").Append(RelicRecord?.GetStatus() ?? "{{C|?}}").Append("]")
                .Append("[Tier ").Append(RelicRecord?.Tier ?? 0).Append("] ")
                .Append(RelicRecord.DisplayName ?? "MISSING_RECORD")
                .ToString()
            ;

        public string OptionDisplayString()
            => OptionDisplayString(this)
            ;

        public static string DebugString(RelicRecord RelicRecord)
        {
            var sB = Event.NewStringBuilder()
                .Append("[").Append(RelicRecord.BaseID).Append("] ").Append(RelicRecord.DisplayName?.Strip() ?? "MISSING").Append("; ")
                .AppendPair(nameof(HasValidRelic), RelicRecord.HasValidRelic).Append("; ")
                .AppendPair(nameof(IsRemainingCached), RelicRecord.IsRemainingCached).Append("; ")
                .AppendPair(nameof(IsDestroyed), RelicRecord.IsDestroyed).Append("; ")
                .AppendPair(nameof(ForReliquary), RelicRecord.ForReliquary).Append("; ")
                .AppendPair(nameof(_Valid), RelicRecord._Valid).Append("; ")
                .AppendPair(nameof(_Pinned), RelicRecord._Pinned);

            return sB.ToString();
        }

        public string DebugString()
            => DebugString(this)
            ;

        public void ViewRelic()
        {
            if (!HasValidRelic
                && !IsPinned())
                return;

            _ = LastHeldBy;

            bool currentlyPlayerHeld = Holder?.IsPlayer() is true;

            var sBDesc = Event.NewStringBuilder(Description);
            using var elements = ScopeDisposedList<StringPair>.GetFromPool();
            if (IsClaimed)
            {
                elements.Add(new("have", "been {{G|claimed by you}}"));
            }

            if (IsCached
                && !IsExitingCache)
            {
                elements.Add(new("are", "currently cached".Colored("C")));
            }

            if (ForReliquary > 0)
            {
                elements.Add(new("are", $"inside the reliquary of {HistoryAPI.GetSultanForPeriod(ForReliquary).entity.Name}, the {Grammar.Ordinal(ForReliquary)} era sultan".Colored("W")));
            }
            else
            {
                if (!currentlyPlayerHeld
                    && (Relic?.CurrentZone == The.ActiveZone
                        || Relic?.InInventory?.CurrentZone == The.ActiveZone))
                {
                    elements.Add(new("are", "somewhere {{W|in this zone}}"));
                }

                if (!LastHeldBy.IsNullOrEmpty())
                {
                    if (currentlyPlayerHeld)
                        elements.Add(new(null, "currently {{G|in your possession}}"));
                    else
                    {
                        string lastHeldBy = LastHeldByPlayer
                            ? $"{"you".Colored("g")}, although you {"don't currently".Colored("W")} possess {it}"
                            : LastHeldBy.Colored("W");

                        elements.Add(new("", $"was last held by {lastHeldBy}"));
                    }
                }
                else
                if (!IsCached
                    || IsExitingCache)
                    elements.Add(new("are", "in an {{r|unknown}} last locaiton"));

                if (elements.IsNullOrEmpty())
                {
                    elements.Add(new("have", "become {{r|untethered from reality}}"));
                }
            }

            sBDesc.AppendLine().AppendLine()
                .Append(IndicativeProximal).Append(" ").Append(IsPlural ? "relics" : "relic").Append(" ")
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
                sBDesc.AppendLine()
                    .Append(IndicativeProximal).Append(" ").Append(IsPlural ? "relics" : "relic").Append(" ")
                    .Append(@is).Append(" ").AppendColored("r", "no more").Append("; ")
                    .Append(it).Append(" ").Append(IsPlural ? "have" : "has").Append(" been irrevocably lost.");

            var sBName = Event.NewStringBuilder(DisplayName)
                .Append('\n')
                .AppendColored("C", $": Tier {Tier} :");

            var buttons = new List<QudMenuItem>(PopupMessage.SingleButton);

            if (!Story.IsNullOrEmpty())
                buttons.Add(new QudMenuItem
                {
                    command = "Story",
                    hotkey = "S",
                    text = "Recall {{W|S}}tory"
                });

            if (Popup.NewPopupMessageAsync(
                    message: sBDesc.ToString(),
                    buttons: buttons,
                    contextTitle: sBName.ToString(),
                    contextRender: Render
                ).Result.command == "Story")
            {
                BookUI.ShowBookByID(Story);
            }
        }

        public void Dispose()
        {
            if (ForReliquary > 0)
                Relic.Release();

            GameObjectReference.Free(ref RelicReference);
            _Tier = null;
            _DisplayName = null;
            _RelicName = null;
            _IndicativeProximal = null;
            _IsPlural = null;
            _it = null;
            _is = null;
            _itIs = null;
            _Render = null;
            _Description = null;
            _Story = null;
            _LastHeldBy = null;
            _LastHeldByPlayer = false;
            _ForReliquary = 0;
            _IsClaimed = false;
            _IsDestroyed = false;
            _Valid = false;
            _Pinned = false;
            _Synched = false;
        }
    }
}
