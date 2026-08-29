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
        protected Guid _TrackerID;
        public Guid TrackerID
        {
            get
            {
                if (_TrackerID.IsEmptyOrDefault())
                    TrackerID = Guid.NewGuid();
                return _TrackerID;
            }
            protected set => _TrackerID = value;
        }

        [NonSerialized]
        public RelicRecord RelicRecord;

        private bool Update;

        public UD_RelicTracker()
            : base()
        { }

        public override void Write(GameObject Basis, SerializationWriter Writer)
        {
            base.Write(Basis, Writer);
            Writer.Write(TrackerID);
            Writer.Write(Update);
        }

        public override void Read(GameObject Basis, SerializationReader Reader)
        {
            base.Read(Basis, Reader);
            _TrackerID = Reader.ReadGuid();
            Update = Reader.ReadBoolean();
        }

        public override void FinalizeRead(SerializationReader Reader)
        {
            base.FinalizeRead(Reader);

            if (RelicTrackerSystem.Instance is RelicTrackerSystem relicTrackerSystem)
                relicTrackerSystem.SyncRelicRecord(ref RelicRecord, this);
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
            {
                originalRecord.Unpin(); // possibly issue?
                relicTrackerSystem?.RemoveRelic(originalRecord);
            }

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
                RelicRecord?.Dispose();

            base.Remove();
        }

        public UD_RelicTracker Init(RelicRecord RelicRecord)
        {
            if (RelicRecord == null)
                return null;

            if (RelicRecord == this.RelicRecord
                && this.RelicRecord.TrackerID == TrackerID)
                return this;

            this.RelicRecord = RelicRecord;
            this.RelicRecord.ParentTracker = this;

            SetTrackerID(RelicRecord.TrackerID);

            this.RelicRecord.Init();
            return ParentObject == RelicRecord.Relic
                ? this
                : null
                ;
        }

        public void SetTrackerID(Guid TrackerID)
        {
            bool registerTrackerForSync = false;
            var relicTrackerSystem = RelicTrackerSystem.Instance;

            if (relicTrackerSystem != null)
                registerTrackerForSync = relicTrackerSystem.UnregisterTrackerForSync(this);

            this.TrackerID = TrackerID;

            if (registerTrackerForSync)
                relicTrackerSystem.RegisterTrackerForSync(this);
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
            || ID == ReplicaCreatedEvent.ID
            || ID == AddedToInventoryEvent.ID
            || ID == StackCountChangedEvent.ID
            || ID == OnDestroyObjectEvent.ID
            || ID == ZoneThawedEvent.ID
            || ID == ZoneActivatedEvent.ID
            || ID == GetDebugInternalsEvent.ID
            ;

        public override bool HandleEvent(ReplicaCreatedEvent E)
        {
            if (ParentObject == E.Object
                && E.Context == "AscendLunarRegent")
            {
                ParentObject.RemovePart(this);
                return true;
            }
            return base.HandleEvent(E);
        }

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
                    RelicRecord?.Destroy();
                else
                    RelicRecord?.Dispose();
            }

            return base.HandleEvent(E);
        }

        public override bool HandleEvent(ZoneThawedEvent E)
        {
            if (ParentObject?.CurrentZone == E.Zone)
                RelicRecord.Unpin();

            return base.HandleEvent(E);
        }

        public override bool HandleEvent(ZoneActivatedEvent E)
        {
            if (ParentObject?.CurrentZone == E.Zone)
                Update = true;

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
                RelicRecord?.Pin();
            }

            return base.FireEvent(E);
        }

        public override bool HandleEvent(GetDebugInternalsEvent E)
        {
            E.AddEntry(this, nameof(TrackerID), TrackerID.ToString());
            E.AddEntry(this, nameof(Update), Update);
            E.AddEntry(this, nameof(RelicRecord), RelicRecord?.GetDebugLines(FieldsOnly: true)?.Aggregate((string)null, Utils.NewLineDelimitedAggregator));
            return base.HandleEvent(E);
        }
    }
}
