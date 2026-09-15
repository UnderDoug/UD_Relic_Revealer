using System;
using System.Collections.Generic;

using XRL;
using XRL.Core;
using XRL.World;

namespace UD_Relic_Revealer.Mod
{
    /// <summary>
    /// Attempts to process a zone with the <see cref="RelicTrackerSystem"/> after the zone has been actiivated but before the player is handed control.
    /// </summary>
    [Serializable]
    public class AfterZoneActivatedCommand : IActionCommand, IComposite
    {
        private static readonly AfterZoneActivatedCommand Instance = new();

        private RelicTrackerSystem System => RelicTrackerSystem.Instance;

        private Zone Zone;
        private Type Issuer;

        public AfterZoneActivatedCommand()
        { }

        public void Write(SerializationWriter Writer)
        {
            Writer.WriteOptimized(Zone?.ZoneID);
            Writer.WriteOptimized(Issuer?.Namespace);
            Writer.WriteOptimized(Issuer?.Name);
        }

        public void Read(SerializationReader Reader)
        {
            if (Reader.ReadOptimizedString() is string readZoneID)
                Zone = The.ZoneManager.GetZone(readZoneID);

            string readNamespace = Reader.ReadOptimizedString();
            string readTypeName = Reader.ReadOptimizedString();
            if (!readNamespace.IsNullOrEmpty()
                && !readTypeName.IsNullOrEmpty())
                Issuer = ModManager.ResolveType(readNamespace, readTypeName);
        }

        public static void Issue(
            Zone Zone,
            MinEvent FromEvent = null
            )
        {
            Instance.Set(Zone, FromEvent?.GetType());
            var actionManager = The.ActionManager;
            if (!actionManager.HasAction<AfterZoneActivatedCommand>())
                actionManager.EnqueueAction(Instance);
        }

        public void Execute(XRLGame Game, ActionManager Manager)
        {
            try
            {
                System?.ProcessZone(Zone, Issuer);
            }
            finally
            {
                Reset(Manager);
            }
        }

        private void Set(
            Zone Zone,
            Type Issuer = null
            )
        {
            this.Zone = Zone;
            this.Issuer = Issuer;
        }

        public void Reset(ActionManager Manager)
        {
            Zone = null;
            Issuer = null;
            Manager?.DequeueActionsDescendedFrom<AfterZoneActivatedCommand>();
        }

        public void Reset()
            => Reset(null)
            ;
    }
}