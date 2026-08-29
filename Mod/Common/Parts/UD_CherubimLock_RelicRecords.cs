using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using UD_Relic_Revealer.Mod;

using SerializeField = UnityEngine.SerializeField;

namespace XRL.World.Parts
{
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
