using System;

using ConsoleLib.Console;

using XRL.UI;

using UD_Relic_Revealer.Mod;

using Options = UD_Relic_Revealer.Options;

namespace XRL.World.Parts
{
    /// <summary>
    /// Provides and manages activated abilities for the two main components of this mod: the relic tracking UI, and the cherubim viewing UI.
    /// </summary>
    [Serializable]
    public class UD_Player_RelicRevealer : IPlayerPart
    {
        public static string RevealRelicsCommand => "UD_Relic_Revealer::RevealRelicsCmd";
        public static string RevealCherubimCommand => "UD_Relic_Revealer::RevealCherubimCmd";

        public Renderable RevealerIcon = new(RelicTrackerSystem.RevealerIcon);

        public Guid RevealRelics_ActivatedAbilityID;
        public Guid RevealCherubim_ActivatedAbilityID;

        private bool Silent;

        public static bool HasShown;

        public UD_Player_RelicRevealer()
            : base()
        { }

        public override void Write(GameObject Basis, SerializationWriter Writer)
        {
            Writer.WriteNamedFields(this, GetType());
            Writer.Write(Silent);
        }

        public override void Read(GameObject Basis, SerializationReader Reader)
        {
            Reader.ReadNamedFields(this, GetType());
            Silent = Reader.ReadBoolean();
        }

        public override void Attach()
        {
            base.Attach();
            if (Options.EnableRelicRevealerActivatedAbility)
                AddAbilities(ParentObject, RemoveFirst: true);
        }

        public override void Remove()
        {
            base.Remove();
            RemoveAbilities(ParentObject);
        }

        public void AddAbilities(GameObject Who = null, bool RemoveFirst = false)
        {
            if (RemoveFirst)
                RemoveAbilities(Who);

            if (RevealRelics_ActivatedAbilityID.IsEmptyOrDefault())
            {
                RevealRelics_ActivatedAbilityID = AddMyActivatedAbility(
                    Name: "Track Relics",
                    Command: RevealRelicsCommand,
                    Class: "Cheat",
                    Description: null,
                    Icon: "R",
                    IsWorldMapUsable: true,
                    Silent: Silent,
                    who: Who);
                Silent = true;
            }
            if (RevealCherubim_ActivatedAbilityID.IsEmptyOrDefault())
            {
                RevealCherubim_ActivatedAbilityID = AddMyActivatedAbility(
                    Name: "View Cherubim",
                    Command: RevealCherubimCommand,
                    Class: "Cheat",
                    Description: null,
                    Icon: "C",
                    IsWorldMapUsable: true,
                    Silent: Silent,
                    who: Who);
                Silent = true;
            }
        }

        public void RemoveAbilities(GameObject Who = null)
        {
            RemoveMyActivatedAbility(ref RevealRelics_ActivatedAbilityID, Who);
            RemoveMyActivatedAbility(ref RevealCherubim_ActivatedAbilityID, Who);
        }

        public override bool WantEvent(int ID, int Cascade)
            => base.WantEvent(ID, Cascade)
            || ID == CommandEvent.ID
            || (!HasShown && ID == BeforeTakeActionEvent.ID)
            ;

        public override bool HandleEvent(CommandEvent E)
        {
            if (E.Command == RevealRelicsCommand)
            {
                var result = RelicTrackerSystem.Instance?.RevealRelics(Context: "ability")
                    ?? UD_Relic_Revealer.Mod.UI.UIUtils.CascadableResult.Back;

                if (!result.IsContinue()
                    && !result.IsSilent())
                    Popup.ShowSpace(
                        Message: "There was an issue trying to display the relic tracker.",
                        Title: "{{R|Relic Revealer Error}}",
                        AfterRender: RelicTrackerSystem.NoRelicsIcon);
            }
            else
            if (E.Command == RevealCherubimCommand)
            {
                var result = RelicTrackerSystem.Instance?.RevealCherubim(Context: "ability")
                    ?? UD_Relic_Revealer.Mod.UI.UIUtils.CascadableResult.Back;
                if (!result.IsContinue()
                    && !result.IsSilent())
                    Popup.ShowSpace(
                        Message: "There was an issue trying to display the cherubim viewer.",
                        Title: "{{R|Cheribum Revealer Error}}",
                        AfterRender: RelicTrackerSystem.NoRelicsIcon);
            }

            return base.HandleEvent(E);
        }

        public override bool HandleEvent(BeforeTakeActionEvent E)
        {
            if (Options.EnableReshowOnGameLoad
                && RelicTrackerSystem.Instance is RelicTrackerSystem relicTrackerSystem)
            {
                HasShown = true;
                relicTrackerSystem.HasShown = true;
                relicTrackerSystem.AskRevealWhat();
            }
            return base.HandleEvent(E);
        }
    }
}
