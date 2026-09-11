using System;

using UD_Relic_Revealer.Mod;

namespace XRL.World.Parts
{
    /// <summary>
    /// Extends <see cref="CherubimLock"/> to call <see cref="RelicTrackerSystem.ProcessRobberChimesTriggered"/> at the appropriate time.
    /// </summary>
    [Serializable]
    public class UD_CherubimLock_RelicRecords : IPartExtension<CherubimLock>
    {
        public bool ProcessedRobberChimes;

        public override void Register(GameObject Object, IEventRegistrar Registrar)
        {
            Registrar.Register("AfterContentsTaken");
            base.Register(Object, Registrar);
        }

        public override bool FireEvent(Event E)
        {
            if (E.ID == "AfterContentsTaken"
                && !ProcessedRobberChimes)
                ProcessedRobberChimes = RelicTrackerSystem.Instance.ProcessRobberChimesTriggered(ParentObject);

            return base.FireEvent(E);
        }
    }
}
