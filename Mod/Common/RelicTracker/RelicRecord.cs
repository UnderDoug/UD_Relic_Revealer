using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using ConsoleLib.Console;

using Qud.UI;

using XRL;
using XRL.UI;
using XRL.World;
using XRL.World.Parts;

namespace UD_Relic_Revealer.Mod
{
    [Serializable]
    public class RelicRecord : IComposite
    {
        public int? _BaseID;
        public int BaseID => (_BaseID = Relic?.BaseID ?? _BaseID) ?? 0;

        public int Tier => Relic?.GetTier() ?? 0;

        public string DisplayName => Relic?.GetDisplayName(AsIfKnown: true, Short: true, Reference: true);

        private IRenderable _Render;
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

        public string Description => Relic?.GetPart<Description>()?.GetLongDescription();

        public string Story => Relic?.GetPropertyOrTag("Story");

        private string _LastHeldBy;
        public string LastHeldBy
        {
            get
            {
                if (Relic?.Holder is GameObject holder)
                    _LastHeldBy = holder.GetDisplayName(AsIfKnown: true, Short: true, Reference: true);

                return _LastHeldBy;
            }
        }

        private bool _IsClaimed;
        public bool IsClaimed
        {
            get => _IsClaimed;
            protected set => _IsClaimed = value;
        }

        public bool IsCached => Relic != null && (The.ZoneManager?.CachedObjects?.Values).IteratorSafe().Any(go => go.BaseID == Relic.BaseID);

        private GameObject _Relic;
        public GameObject Relic
        {
            get => _Relic;
            protected set => _Relic = value;
        }

        public RelicRecord()
        { }

        public RelicRecord(GameObject Relic, bool IsCached = true)
            : this()
        {
            this.Relic = Relic;
        }

        public void Write(SerializationWriter Writer)
        {
            Writer.WriteNullable(BaseID);
            Writer.WriteGameObject(Relic);
            Writer.WriteOptimized(LastHeldBy);
            Writer.Write(IsClaimed);
        }

        public void Read(SerializationReader Reader)
        {
            _BaseID = Reader.ReadNullableInt32();
            Relic = Reader.ReadGameObject();
            _LastHeldBy = Reader.ReadOptimizedString();
            _IsClaimed = Reader.ReadBoolean();
        }

        public void ViewRelic()
        {
            if (Relic == null)
                return;

            int epistemicStatus = -1;
            var examiner = Relic.GetPart<Examiner>();
            if (examiner != null)
            {
                epistemicStatus = examiner.EpistemicStatus;
                examiner.EpistemicStatus = Examiner.EPISTEMIC_STATUS_KNOWN;
                //Relic.RemovePart(examiner);
            }

            try
            {
                var tooltipInformation = Look.GenerateTooltipInformation(Relic);

                var sBDesc = Event.NewStringBuilder().Append(tooltipInformation.LongDescription);

                var sBName = Event.NewStringBuilder(tooltipInformation.DisplayName)
                    .Append('\n')
                    .AppendColored("C", $": Tier {Tier} :");

                var buttons = new List<QudMenuItem>(PopupMessage.SingleButton);

                if (!Story.IsNullOrEmpty())
                {
                    buttons.Add(new QudMenuItem
                    {
                        command = "Story",
                        hotkey = "S",
                        text = "Recall {{W|S}}tory"
                    });
                }

                if (Popup.NewPopupMessageAsync(
                        message: sBDesc.ToString(),
                        buttons: buttons,
                        contextTitle: sBName.ToString(),
                        contextRender: Render ?? tooltipInformation.IconRenderable
                    ).Result.command == "Story")
                {
                    BookUI.ShowBookByID(Story);
                }
            }
            finally
            {
                if (examiner != null)
                {
                    examiner.EpistemicStatus = epistemicStatus;
                    //Relic.AddPart(examiner);
                }
            }
        }
    }
}
