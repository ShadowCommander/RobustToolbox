using System.Collections.Generic;
using Robust.Shared.Containers;
using Robust.Shared.IoC;
using Robust.Shared.Map;

namespace Robust.Shared.GameObjects
{
    /// <summary>
    ///     Handles moving entities between grids as they move around.
    /// </summary>
    internal sealed class SharedGridTraversalSystem : EntitySystem
    {
        [Dependency] private readonly IMapManager _mapManager = default!;

        private Queue<MoveEvent> _queuedMoveEvents = new();

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<MoveEvent>(QueueMoveEvent);
        }

        public override void Shutdown()
        {
            base.Shutdown();
            UnsubscribeLocalEvent<MoveEvent>();
        }

        public override void Update(float frameTime)
        {
            base.Update(frameTime);
            while (_queuedMoveEvents.Count > 0)
            {
                var moveEvent = _queuedMoveEvents.Dequeue();
                var entity = moveEvent.Sender;

                if (entity.Deleted || !entity.HasComponent<PhysicsComponent>() || entity.IsInContainer()) continue;

                var transform = entity.Transform;
                // Change parent if necessary
                // Given islands will probably have a bunch of static bodies in them then we'll verify velocities first as it's way cheaper

                // This shoouullddnnn'''tt de-parent anything in a container because none of that should have physics applied to it.
                if (_mapManager.TryFindGridAt(transform.MapID, moveEvent.NewPosition.ToMapPos(EntityManager), out var grid) &&
                    grid.GridEntityId.IsValid() &&
                    grid.GridEntityId != entity.Uid)
                {
                    // Also this may deparent if 2 entities are parented but not using containers so fix that
                    if (grid.Index != transform.GridID)
                    {
                        transform.AttachParent(EntityManager.GetEntity(grid.GridEntityId));
                    }
                }
                else
                {
                    transform.AttachParent(_mapManager.GetMapEntity(transform.MapID));
                }
            }
        }

        private void QueueMoveEvent(MoveEvent moveEvent)
        {
            _queuedMoveEvents.Enqueue(moveEvent);
        }
    }
}
