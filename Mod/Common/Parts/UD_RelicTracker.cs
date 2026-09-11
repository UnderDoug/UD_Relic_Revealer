using System;
using System.Linq;

using UD_Relic_Revealer.Mod;

namespace XRL.World.Parts
{
    /// <summary>
    /// Helper part to ensure the <see cref="RelicTrackerSystem"/>'s cached <see cref="UD_Relic_Revealer.Mod.RelicRecord"/>s are synced with the relic <see cref="GameObject"/> across serialization/deserialization.
    /// </summary>
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

        /// <remarks>
        /// Checks the <paramref name="Source"/> for its original <see cref="RelicRecord"/> and, 
        /// if <paramref name="CopyID"/> is <see langword="true"/> and the original <see cref="RelicRecord.IsExitingCache"/>, 
        /// removes the original record from the <see cref="RelicTrackerSystem"/>.<br/>
        /// If the <see cref="RelicTrackerSystem"/> is <see langword="null"/>, or <see cref="RelicRecord"/> is invalid, removes <see langword="this"/> from its <see cref="IPart.ParentObject"/>.
        /// </remarks>
        public override void FinalizeCopyLate(GameObject Source, bool CopyEffects, bool CopyID, Func<GameObject, GameObject> MapInv)
        {
            base.FinalizeCopyLate(Source, CopyEffects, CopyID, MapInv);

            var relicTrackerSystem = RelicTrackerSystem.Instance;

            var originalRecord = Source.GetPart<UD_RelicTracker>()?.RelicRecord;

            if (CopyID
                && originalRecord?.IsExitingCache is true)
            {
                originalRecord.Unpin(); // possibly issue?
                relicTrackerSystem?.RemoveRelic(originalRecord);
            }

            if (relicTrackerSystem == null
                || !relicTrackerSystem.TryRecordDuplicateRelic(ParentObject, originalRecord, out RelicRecord)
                || RelicRecord == null
                || RelicRecord.BaseID == 0)
            {
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

        /// <summary>
        /// Assigns <paramref name="RelicRecord"/> to <see cref="RelicRecord"/> and aligns <see cref="TrackerID"/> with its <see cref="RelicRecord.TrackerID"/>. 
        /// </summary>
        /// <param name="RelicRecord">The record whose <see cref="RelicRecord.TrackerID"/> to align with.</param>
        /// <param name="InitRecord">Whether or not <see cref="RelicRecord"/> should call <see cref="RelicRecord.Init"/> once assigned.</param>
        /// <returns><see langword="this"/> if <paramref name="RelicRecord"/> is not <see langword="null"/>; otherwise, <br/><see langword="null"/></returns>
        public UD_RelicTracker Init(RelicRecord RelicRecord, bool InitRecord = false)
        {
            if (RelicRecord == null)
                return null;

            if (RelicRecord == this.RelicRecord
                && this.RelicRecord.TrackerID == TrackerID)
                return this;

            this.RelicRecord = RelicRecord;
            this.RelicRecord.ParentTracker = this;

            SetTrackerID(RelicRecord.TrackerID);

            if (InitRecord)
                this.RelicRecord.Init();

            return this;
        }

        /// <summary>
        /// Sets <see cref="TrackerID"/> to <paramref name="TrackerID"/>, unregiestering and re-registering <see langword="this"/> from <see cref="RelicTrackerSystem._TrackersWantingRecords"/> where applicable.
        /// </summary>
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
