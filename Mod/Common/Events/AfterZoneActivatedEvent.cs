using System;
using System.Collections.Generic;
using System.Text;

using XRL;
using XRL.World;

namespace UD_Relic_Revealer.Mod.Events
{
    public class AfterZoneActivatedEvent : ModSingletonEvent<AfterZoneActivatedEvent>
    {
        public static readonly string RegisteredEventID = nameof(AfterZoneActivatedEvent);

        public new static readonly int CascadeLevel = CASCADE_ALL;

        public Zone Zone;

        public AfterZoneActivatedEvent()
            : base()
        { }

        public override int GetCascadeLevel()
            => CascadeLevel
            ;

        public virtual string GetRegisteredEventID()
            => RegisteredEventID
            ;

        public override void Reset()
        {
            base.Reset();
            Zone = null;
        }

        public static void Send(Zone Zone)
        {
            Instance.Zone = Zone;
            The.Game.HandleEvent(Instance);
            Zone.HandleEvent(Instance);
            Instance.Reset();
        }
    }
}
