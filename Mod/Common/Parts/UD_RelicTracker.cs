using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using UD_Relic_Revealer.Mod;

using SerializeField = UnityEngine.SerializeField;

namespace XRL.World.Parts
{
    [Serializable]
    public class UD_RelicTracker : IScribedPart
    {
        public RelicRecord RelicRecord;

        [SerializeField]
        private bool Update;

        public UD_RelicTracker()
            : base()
        { }

        public override void FinalizeRead(SerializationReader Reader)
        {
            base.FinalizeRead(Reader);
            if (RelicTrackerSystem.Instance is RelicTrackerSystem relicTrackerSystem)
                relicTrackerSystem.SyncRelicRecord(RelicRecord);
        }

        public override IPart DeepCopy(GameObject Parent, Func<GameObject, GameObject> MapInv)
        {
            if (base.DeepCopy(Parent, MapInv) is not UD_RelicTracker copy)
                return null;

            copy.Update = true;

            return copy;
        }

        public override void FinalizeCopyLate(GameObject Source, bool CopyEffects, bool CopyID, Func<GameObject, GameObject> MapInv)
        {
            base.FinalizeCopyLate(Source, CopyEffects, CopyID, MapInv);

            /*Utils.Log($"{nameof(UD_RelicTracker)}.{nameof(FinalizeCopyLate)} for {ParentObject?.DebugName ?? "NO_OBJECT"}");*/
            var relicTrackerSystem = RelicTrackerSystem.Instance;

            /*Utils.Log($"  {nameof(relicTrackerSystem)} not null: {relicTrackerSystem != null}");*/

            var originalRecord = Source.GetPart<UD_RelicTracker>()?.RelicRecord;

            /*Utils.Log($"  {nameof(CopyID)}: {CopyID}");
            Utils.Log($"  {nameof(originalRecord.IsExitingCache)}: {originalRecord?.IsExitingCache is true}");
            Utils.Log($"  {nameof(relicTrackerSystem.CachedRelicRecords)}.{nameof(ICollection<RelicRecord>.Contains)}({nameof(originalRecord)}): {relicTrackerSystem?.CachedRelicRecords?.Contains(originalRecord) is true}");

            (relicTrackerSystem?.CachedRelicRecords).IteratorSafe().Loggregate(
                Proc: RelicRecord.DebugString,
                Empty: "no records",
                PostProc: s => $"    : {s}");*/

            if (CopyID
                && originalRecord?.IsExitingCache is true)
                relicTrackerSystem?.RemoveRelic(originalRecord);

            bool? recorded = null;
            if (relicTrackerSystem == null
                || ((recorded = relicTrackerSystem.TryRecordDuplicateRelic(ParentObject, originalRecord, out RelicRecord)) is not true)
                || RelicRecord == null
                || RelicRecord.BaseID == 0)
            {
                /*Utils.Log($"  {nameof(recorded)}: {recorded?.ToString() ?? "null"}");
                Utils.Log($"  {nameof(RelicRecord)} not null: {RelicRecord != null}");*/
                ParentObject.RemovePart(this);
                return;
            }
        }

        public override void Remove()
        {
            if (!RelicRecord.IsDestroyed)
                RelicTrackerSystem.Instance?.RemoveRelic(RelicRecord);
            base.Remove();
        }

        public override bool WantTurnTick()
            => true
            ;

        public override void TurnTick(long TimeTick, int Amount)
        {
            if (Update)
            {
                Update = false;
                _ = RelicRecord?.LastHeldBy;
            }
            base.TurnTick(TimeTick, Amount);
        }

        public override void Register(GameObject Object, IEventRegistrar Registrar)
        {
            Registrar.Register("CommandTakeObject");
            Registrar.Register("ZoneFreezing");
            base.Register(Object, Registrar);
        }

        public override bool WantEvent(int ID, int Cascade)
            => base.WantEvent(ID, Cascade)
            || ID == AddedToInventoryEvent.ID
            || ID == StackCountChangedEvent.ID
            || ID == OnDestroyObjectEvent.ID
            || ID == ZoneThawedEvent.ID
            ;

        public override bool HandleEvent(AddedToInventoryEvent E)
        {
            if (ParentObject == E.Item)
                Update = true;

            return base.HandleEvent(E);
        }

        public override bool HandleEvent(StackCountChangedEvent E)
        {
            if (ParentObject == E.Object)
                RelicRecord.RefreshCache(Force: true);

            return base.HandleEvent(E);
        }

        public override bool HandleEvent(OnDestroyObjectEvent E)
        {
            if (ParentObject == E.Object)
            {
                if (!E.Silent)
                    RelicRecord.Destroy();
                else
                    RelicRecord.Dispose();
            }

            return base.HandleEvent(E);
        }

        public override bool HandleEvent(ZoneThawedEvent E)
        {
            if (ParentObject?.CurrentZone == E.Zone)
                RelicRecord.Unpin();

            return base.HandleEvent(E);
        }

        public override bool FireEvent(Event E)
        {
            if (E.ID == "CommandTakeObject")
            {
                Update = true;
            }
            else
            if (E.ID == "ZoneFreezing")
            {
                RelicRecord?.Pin(ClearRelic: true);
            }

            return base.FireEvent(E);
        }
    }
}
