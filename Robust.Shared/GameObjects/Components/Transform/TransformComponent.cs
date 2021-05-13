using System;
using System.Collections.Generic;
using System.Linq;
using Robust.Shared.Animations;
using Robust.Shared.Containers;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Players;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Utility;
using Robust.Shared.ViewVariables;

namespace Robust.Shared.GameObjects
{
    internal class TransformComponent : Component, ITransformComponent, IComponentDebug
    {
        [DataField("parent")]
        private EntityUid _parent;
        [DataField("pos")]
        private Vector2 _localPosition = Vector2.Zero; // holds offset from grid, or offset from parent
        [DataField("rot")]
        private Angle _localRotation; // local rotation
        [DataField("noRot")]
        private bool _noLocalRotation;

        private Matrix3 _localMatrix = Matrix3.Identity;
        private Matrix3 _invLocalMatrix = Matrix3.Identity;

        private Vector2? _nextPosition;
        private Angle? _nextRotation;

        private Vector2 _prevPosition;
        private Angle _prevRotation;

        // Cache changes so we can distribute them after physics is done (better cache)
        private EntityCoordinates? _oldCoords;
        private Angle? _oldLocalRotation;

        public bool UpdatesDeferred => _oldCoords != null || _oldLocalRotation != null;

        [ViewVariables(VVAccess.ReadWrite)]
        public bool ActivelyLerping { get; set; }

        [ViewVariables] private readonly SortedSet<EntityUid> _children = new();

        [Dependency] private readonly IMapManager _mapManager = default!;

        /// <inheritdoc />
        public override string Name => "Transform";

        /// <inheritdoc />
        public sealed override uint? NetID => NetIDs.TRANSFORM;

        /// <inheritdoc />
        [ViewVariables]
        public MapId MapID { get; private set; }

        private bool _mapIdInitialized;

        public bool DeferUpdates { get; set; }

        /// <inheritdoc />
        [ViewVariables]
        public GridId GridID
        {
            get
            {
                // root node, grid id is undefined
                if (Owner.HasComponent<IMapComponent>())
                    return GridId.Invalid;

                // second level node, terminates recursion up the branch of the tree
                if (Owner.TryGetComponent(out IMapGridComponent? gridComp))
                    return gridComp.GridIndex;

                // branch or leaf node
                if (_parent.IsValid())
                    return Parent!.GridID;

                // Not on a grid
                return GridId.Invalid;
            }
        }

        /// <inheritdoc />
        [ViewVariables(VVAccess.ReadWrite)]
        public bool NoLocalRotation
        {
            get => _noLocalRotation;
            set
            {
                if (value)
                    LocalRotation = Angle.Zero;

                _noLocalRotation = value;
                Dirty();
            }
        }

        /// <inheritdoc />
        [ViewVariables(VVAccess.ReadWrite)]
        [Animatable]
        public Angle LocalRotation
        {
            get => _localRotation;
            set
            {
                if(_noLocalRotation)
                    return;

                if (_localRotation.EqualsApprox(value, 0.00001))
                    return;

                var oldRotation = _localRotation;

                // Set _nextRotation to null to break any active lerps if this is a client side prediction.
                _nextRotation = null;
                SetRotation(value);
                Dirty();

                if (!DeferUpdates)
                {
                    RebuildMatrices();
                    Owner.EntityManager.EventBus.RaiseLocalEvent(Owner.Uid, new RotateEvent(Owner, oldRotation, _localRotation));
                }
                else
                {
                    _oldLocalRotation ??= oldRotation;
                }
            }
        }

        /// <inheritdoc />
        [ViewVariables(VVAccess.ReadWrite)]
        public Angle WorldRotation
        {
            get
            {
                if (_parent.IsValid())
                {
                    return Parent!.WorldRotation + _localRotation;
                }

                return _localRotation;
            }
            set
            {
                var current = WorldRotation;
                var diff = value - current;
                LocalRotation += diff;
            }
        }

        /// <summary>
        ///     Current parent entity of this entity.
        /// </summary>
        [ViewVariables]
        public ITransformComponent? Parent
        {
            get => !_parent.IsValid() ? null : Owner.EntityManager.GetEntity(_parent).Transform;
            set
            {
                if (value != null)
                {
                    AttachParent(value);
                }
                else
                {
                    AttachToGridOrMap();
                }
            }
        }

        [ViewVariables(VVAccess.ReadWrite)]
        public EntityUid ParentUid
        {
            get => _parent;
            set => Parent = Owner.EntityManager.GetEntity(value).Transform;
        }

        /// <inheritdoc />
        public Matrix3 WorldMatrix
        {
            get
            {
                if (_parent.IsValid())
                {
                    var parentMatrix = Parent!.WorldMatrix;
                    var myMatrix = GetLocalMatrix();
                    Matrix3.Multiply(ref myMatrix, ref parentMatrix, out var result);
                    return result;
                }

                return GetLocalMatrix();
            }
        }

        /// <inheritdoc />
        public Matrix3 InvWorldMatrix
        {
            get
            {
                if (_parent.IsValid())
                {
                    var matP = Parent!.InvWorldMatrix;
                    var myMatrix = GetLocalMatrixInv();
                    Matrix3.Multiply(ref matP, ref myMatrix, out var result);
                    return result;
                }

                return GetLocalMatrixInv();
            }
        }

        public bool IsMapTransform => !Owner.IsInContainer();

        /// <inheritdoc />
        [ViewVariables(VVAccess.ReadWrite)]
        [Animatable]
        public Vector2 WorldPosition
        {
            get
            {
                if (_parent.IsValid())
                {
                    // parent coords to world coords
                    return Parent!.WorldMatrix.Transform(_localPosition);
                }
                else
                {
                    return Vector2.Zero;
                }
            }
            set
            {
                if (!_parent.IsValid())
                {
                    DebugTools.Assert("Parent is invalid while attempting to set WorldPosition - did you try to move root node?");
                    return;
                }

                // world coords to parent coords
                var newPos = Parent!.InvWorldMatrix.Transform(value);

                // float rounding error guard, if the offset is less than 1mm ignore it
                //if ((newPos - GetLocalPosition()).LengthSquared < 1.0E-3)
                //    return;

                LocalPosition = newPos;
            }
        }

        [ViewVariables(VVAccess.ReadWrite)]
        public EntityCoordinates Coordinates
        {
            get
            {
                var valid = _parent.IsValid();
                return new EntityCoordinates(valid ? _parent : Owner.Uid, valid ? LocalPosition : Vector2.Zero);
            }
            set
            {
                var oldPosition = Coordinates;

                if (value.EntityId != _parent)
                {
                    var newEntity = Owner.EntityManager.GetEntity(value.EntityId);
                    AttachParent(newEntity);
                }

                _localPosition = value.Position;
                Dirty();

                if (!DeferUpdates)
                {
                    //TODO: This is a hack, look into WHY we can't call GridPosition before the comp is Running
                    if (Running)
                    {
                        RebuildMatrices();
                        Owner.EntityManager.EventBus.RaiseLocalEvent(Owner.Uid, new MoveEvent(Owner, oldPosition, Coordinates));
                    }
                }
                else
                {
                    _oldCoords ??= oldPosition;
                }
            }
        }

        [ViewVariables(VVAccess.ReadWrite)]
        public MapCoordinates MapPosition => new(WorldPosition, MapID);

        [ViewVariables(VVAccess.ReadWrite)]
        [Animatable]
        public Vector2 LocalPosition
        {
            get => _localPosition;
            set
            {
                if (_localPosition.EqualsApprox(value, 0.00001))
                    return;

                // Set _nextPosition to null to break any on-going lerps if this is done in a client side prediction.
                _nextPosition = null;

                var oldGridPos = Coordinates;
                SetPosition(value);
                Dirty();

                if (!DeferUpdates)
                {
                    RebuildMatrices();
                    Owner.EntityManager.EventBus.RaiseLocalEvent(Owner.Uid, new MoveEvent(Owner, oldGridPos, Coordinates));
                }
                else
                {
                    _oldCoords ??= oldGridPos;
                }
            }
        }

        /// <inheritdoc />
        [ViewVariables]
        public bool Anchored
        {
            get => Owner.HasComponent<SnapGridComponent>();
            set
            {
                if(value && !Owner.HasComponent<SnapGridComponent>())
                    Owner.AddComponent<SnapGridComponent>();

                else if(!value && Owner.HasComponent<SnapGridComponent>())
                    Owner.RemoveComponent<SnapGridComponent>();
            }
        }

        [ViewVariables]
        public IEnumerable<ITransformComponent> Children =>
            _children.Select(u => Owner.EntityManager.GetEntity(u).Transform);

        [ViewVariables] public IEnumerable<EntityUid> ChildEntityUids => _children;

        [ViewVariables] public int ChildCount => _children.Count;

        /// <inheritdoc />
        [ViewVariables]
        public Vector2? LerpDestination
        {
            get => _nextPosition;
            set
            {
                _nextPosition = value;
                ActivelyLerping = true;
            }
        }

        [ViewVariables]
        public Angle? LerpAngle
        {
            get => _nextRotation;
            set
            {
                _nextRotation = value;
                ActivelyLerping = true;
            }
        }

        [ViewVariables] public Vector2 LerpSource => _prevPosition;
        [ViewVariables] public Angle LerpSourceAngle => _prevRotation;

        [ViewVariables] public EntityUid LerpParent { get; private set; }

        /// <inheritdoc />
        public override void Initialize()
        {
            base.Initialize();

            // Children MAY be initialized here before their parents are.
            // We do this whole dance to handle this recursively,
            // setting _mapIdInitialized along the way to avoid going to the IMapComponent every iteration.
            static MapId FindMapIdAndSet(TransformComponent p)
            {
                if (p._mapIdInitialized)
                {
                    return p.MapID;
                }

                MapId value;
                if (p._parent.IsValid())
                {
                    value = FindMapIdAndSet((TransformComponent) p.Parent!);
                }
                else
                {
                    // second level node, terminates recursion up the branch of the tree
                    if (p.Owner.TryGetComponent(out IMapComponent? mapComp))
                    {
                        value = mapComp.WorldMap;
                    }
                    else
                    {
                        throw new InvalidOperationException("Transform node does not exist inside scene tree!");
                    }
                }

                p.MapID = value;
                p._mapIdInitialized = true;
                return value;
            }

            if (!_mapIdInitialized)
            {
                FindMapIdAndSet(this);

                _mapIdInitialized = true;
            }

            // Has to be done if _parent is set from ExposeData.
            if (_parent.IsValid())
            {
                // Note that _children is a SortedSet<EntityUid>,
                // so duplicate additions (which will happen) don't matter.
                ((TransformComponent) Parent!)._children.Add(Owner.Uid);
            }
        }

        /// <inheritdoc />
        protected override void Startup()
        {
            base.Startup();

            // Keep the cached matrices in sync with the fields.
            RebuildMatrices();
            Dirty();
        }

        public void RunDeferred(Box2 worldAABB)
        {
            // if we resolved to (close enough) to the OG position then no update.
            if ((_oldCoords == null || _oldCoords.Equals(Coordinates)) &&
                (_oldLocalRotation == null || _oldLocalRotation.Equals(_localRotation)))
            {
                return;
            }

            RebuildMatrices();

            if (_oldCoords != null)
            {
                Owner.EntityManager.EventBus.RaiseLocalEvent(Owner.Uid, new MoveEvent(Owner, _oldCoords.Value, Coordinates, worldAABB));
                _oldCoords = null;
            }

            if (_oldLocalRotation != null)
            {
                Owner.EntityManager.EventBus.RaiseLocalEvent(Owner.Uid, new RotateEvent(Owner, _oldLocalRotation.Value, _localRotation, worldAABB));
                _oldLocalRotation = null;
            }
        }

        /// <summary>
        /// Detaches this entity from its parent.
        /// </summary>
        public void AttachToGridOrMap()
        {
            // nothing to do
            var oldParent = Parent;
            if (oldParent == null)
            {
                return;
            }

            var mapPos = MapPosition;

            IEntity newMapEntity;
            if (_mapManager.TryFindGridAt(mapPos, out var mapGrid))
            {
                newMapEntity = Owner.EntityManager.GetEntity(mapGrid.GridEntityId);
            }
            else if (_mapManager.HasMapEntity(mapPos.MapId))
            {
                newMapEntity = _mapManager.GetMapEntity(mapPos.MapId);
            }
            else
            {
                DetachParentToNull();
                return;
            }

            // this would be a no-op
            var oldParentEnt = oldParent.Owner;
            if (newMapEntity == oldParentEnt)
            {
                return;
            }

            AttachParent(newMapEntity);

            // Technically we're not moving, just changing parent.
            DeferUpdates = true;
            WorldPosition = mapPos.Position;
            DeferUpdates = false;

            Dirty();
        }

        /// <inheritdoc />
        public void DetachParentToNull()
        {
            var oldParent = Parent;
            if (oldParent == null)
            {
                return;
            }

            var oldConcrete = (TransformComponent) oldParent;
            var uid = Owner.Uid;
            oldConcrete._children.Remove(uid);

            var oldParentOwner = oldParent?.Owner;

            var entMessage = new EntParentChangedMessage(Owner, oldParentOwner);
            var compMessage = new ParentChangedMessage(null, oldParentOwner);
            _parent = EntityUid.Invalid;
            Owner.EntityManager.EventBus.RaiseLocalEvent(Owner.Uid, entMessage);
            Owner.SendMessage(this, compMessage);
            var oldMapId = MapID;
            MapID = MapId.Nullspace;

            // Does it even make sense to call these since this is called purely from OnRemove right now?
            RebuildMatrices();
            MapIdChanged(oldMapId);
            Dirty();
        }

        /// <summary>
        /// Sets another entity as the parent entity.
        /// </summary>
        /// <param name="newParent"></param>
        public virtual void AttachParent(ITransformComponent newParent)
        {
            //NOTE: This function must be callable from before initialize

            // nothing to attach to.
            if (ParentUid == newParent.Owner.Uid)
                return;

            DebugTools.Assert(newParent != this,
                $"Can't parent a {nameof(ITransformComponent)} to itself.");

            // That's already our parent, don't bother attaching again.
            var newParentEnt = newParent.Owner;
            if (newParentEnt.Uid == _parent)
            {
                return;
            }

            var oldParent = Parent;
            var oldConcrete = (TransformComponent?) oldParent;
            var uid = Owner.Uid;
            oldConcrete?._children.Remove(uid);
            var newConcrete = (TransformComponent) newParent;
            newConcrete._children.Add(uid);

            var oldParentOwner = oldParent?.Owner;
            var entMessage = new EntParentChangedMessage(Owner, oldParentOwner);
            var compMessage = new ParentChangedMessage(newParentEnt, oldParentOwner);

            // offset position from world to parent
            SetPosition(newParent.InvWorldMatrix.Transform(WorldPosition));
            _parent = newParentEnt.Uid;

            ChangeMapId(newConcrete.MapID);

            Owner.EntityManager.EventBus.RaiseLocalEvent(Owner.Uid, entMessage);
            Owner.SendMessage(this, compMessage);
            
            RebuildMatrices();
            Dirty();
        }

        internal void ChangeMapId(MapId newMapId)
        {
            if (newMapId == MapID)
                return;

            var oldMapId = MapID;

            MapID = newMapId;
            MapIdChanged(oldMapId);
            UpdateChildMapIdsRecursive(MapID, Owner.EntityManager.ComponentManager);
        }

        private void UpdateChildMapIdsRecursive(MapId newMapId, IComponentManager comp)
        {
            foreach (var child in _children)
            {
                var concrete = comp.GetComponent<TransformComponent>(child);
                var old = concrete.MapID;

                concrete.MapID = newMapId;
                concrete.MapIdChanged(old);

                if (concrete.ChildCount != 0)
                {
                    concrete.UpdateChildMapIdsRecursive(newMapId, comp);
                }
            }
        }

        private void MapIdChanged(MapId oldId)
        {
            Owner.EntityManager.EventBus.RaiseEvent(EventSource.Local, new EntMapIdChangedMessage(Owner, oldId));
        }

        public void AttachParent(IEntity parent)
        {
            var transform = parent.Transform;
            AttachParent(transform);
        }

        /// <summary>
        ///     Finds the transform of the entity located on the map itself
        /// </summary>
        public ITransformComponent GetMapTransform()
        {
            if (Parent != null) //If we are not the final transform, query up the chain of parents
            {
                return Parent.GetMapTransform();
            }

            return this;
        }


        /// <summary>
        ///     Does this entity contain the entity in the argument
        /// </summary>
        public bool ContainsEntity(ITransformComponent entityTransform)
        {
            if (entityTransform.Parent == null) //Is the entity the scene root
            {
                return false;
            }

            if (this == entityTransform.Parent) //Is this the direct container of the entity
            {
                return true;
            }
            else
            {
                return
                    ContainsEntity(entityTransform
                        .Parent); //Recursively search up the entities containers for this object
            }
        }

        /// <param name="player"></param>
        /// <inheritdoc />
        public override ComponentState GetComponentState(ICommonSession player)
        {
            return new TransformComponentState(_localPosition, LocalRotation, _parent, _noLocalRotation);
        }

        /// <inheritdoc />
        public override void HandleComponentState(ComponentState? curState, ComponentState? nextState)
        {
            if (curState != null)
            {
                var newState = (TransformComponentState) curState;

                var newParentId = newState.ParentID;
                var rebuildMatrices = false;
                if (Parent?.Owner?.Uid != newParentId)
                {
                    if (newParentId != _parent)
                    {
                        if (!newParentId.IsValid())
                        {
                            DetachParentToNull();
                        }
                        else
                        {
                            var newParent = Owner.EntityManager.GetEntity(newParentId);
                            AttachParent(newParent.Transform);
                        }
                    }

                    rebuildMatrices = true;
                }

                if (LocalRotation != newState.Rotation)
                {
                    SetRotation(newState.Rotation);
                    rebuildMatrices = true;
                }

                if (!_localPosition.EqualsApprox(newState.LocalPosition, 0.0001))
                {
                    var oldPos = Coordinates;
                    SetPosition(newState.LocalPosition);

                    var ev = new MoveEvent(Owner, oldPos, Coordinates);
                    EntitySystem.Get<SharedTransformSystem>().DeferMoveEvent(ev);

                    rebuildMatrices = true;
                }

                _prevPosition = newState.LocalPosition;
                _prevRotation = newState.Rotation;

                if (rebuildMatrices)
                {
                    RebuildMatrices();
                }

                Dirty();
            }

            if (nextState is TransformComponentState nextTransform)
            {
                _nextPosition = nextTransform.LocalPosition;
                _nextRotation = nextTransform.Rotation;
                LerpParent = nextTransform.ParentID;
                ActivateLerp();
            }
            else
            {
                // this should cause the lerp to do nothing
                _nextPosition = null;
                _nextRotation = null;
                LerpParent = EntityUid.Invalid;
            }
        }

        protected virtual void SetPosition(Vector2 position)
        {
            // DebugTools.Assert(!float.IsNaN(position.X) && !float.IsNaN(position.Y));
            _localPosition = position;
        }

        protected virtual void SetRotation(Angle rotation)
        {
            _localRotation = rotation;
        }

        public Matrix3 GetLocalMatrix()
        {
            return _localMatrix;
        }

        public Matrix3 GetLocalMatrixInv()
        {
            return _invLocalMatrix;
        }

        private void RebuildMatrices()
        {
            var pos = _localPosition;

            if (!_parent.IsValid()) // Root Node
                pos = Vector2.Zero;

            var rot = _localRotation.Theta;

            var posMat = Matrix3.CreateTranslation(pos);
            var rotMat = Matrix3.CreateRotation((float) rot);

            Matrix3.Multiply(ref rotMat, ref posMat, out var transMat);

            _localMatrix = transMat;

            var posImat = Matrix3.Invert(posMat);
            var rotImap = Matrix3.Invert(rotMat);

            Matrix3.Multiply(ref posImat, ref rotImap, out var itransMat);

            _invLocalMatrix = itransMat;
        }

        public string GetDebugString()
        {
            return $"pos/rot/wpos/wrot: {Coordinates}/{LocalRotation}/{WorldPosition}/{WorldRotation}";
        }

        private void ActivateLerp()
        {
            if (ActivelyLerping)
            {
                return;
            }

            ActivelyLerping = true;
            Owner.EntityManager.EventBus.RaiseLocalEvent(Owner.Uid, new TransformStartLerpMessage(this));
        }

        /// <summary>
        ///     Serialized state of a TransformComponent.
        /// </summary>
        [Serializable, NetSerializable]
        protected internal class TransformComponentState : ComponentState
        {
            /// <summary>
            ///     Current parent entity of this entity.
            /// </summary>
            public readonly EntityUid ParentID;

            /// <summary>
            ///     Current position offset of the entity.
            /// </summary>
            public readonly Vector2 LocalPosition;

            /// <summary>
            ///     Current rotation offset of the entity.
            /// </summary>
            public readonly Angle Rotation;

            /// <summary>
            /// Is the transform able to be locally rotated?
            /// </summary>
            public readonly bool NoLocalRotation;

            /// <summary>
            ///     Constructs a new state snapshot of a TransformComponent.
            /// </summary>
            /// <param name="localPosition">Current position offset of this entity.</param>
            /// <param name="rotation">Current direction offset of this entity.</param>
            /// <param name="parentId">Current parent transform of this entity.</param>
            /// <param name="noLocalRotation"></param>
            public TransformComponentState(Vector2 localPosition, Angle rotation, EntityUid parentId, bool noLocalRotation)
                : base(NetIDs.TRANSFORM)
            {
                LocalPosition = localPosition;
                Rotation = rotation;
                ParentID = parentId;
                NoLocalRotation = noLocalRotation;
            }
        }
    }

    /// <summary>
    ///     Raised whenever an entity moves.
    ///     There is no guarantee it will be raised if they move in worldspace, only when moved relative to their parent.
    /// </summary>
    public class MoveEvent : HandledEntityEventArgs
    {
        public MoveEvent(IEntity sender, EntityCoordinates oldPos, EntityCoordinates newPos, Box2? worldAABB = null)
        {
            Sender = sender;
            OldPosition = oldPos;
            NewPosition = newPos;
            WorldAABB = worldAABB;
        }

        public IEntity Sender { get; }
        public EntityCoordinates OldPosition { get; }
        public EntityCoordinates NewPosition { get; }

        /// <summary>
        ///     New AABB of the entity.
        /// </summary>
        public Box2? WorldAABB { get; }
    }

    /// <summary>
    ///     Raised whenever this entity rotates in relation to their parent.
    /// </summary>
    public class RotateEvent : EntityEventArgs
    {
        public RotateEvent(IEntity sender, Angle oldRotation, Angle newRotation, Box2? worldAABB = null)
        {
            Sender = sender;
            OldRotation = oldRotation;
            NewRotation = newRotation;
            WorldAABB = worldAABB;
        }

        public IEntity Sender { get; }
        public Angle OldRotation { get; }
        public Angle NewRotation { get; }
        /// <summary>
        ///     New AABB of the entity.
        /// </summary>
        public Box2? WorldAABB { get; }
    }
}
