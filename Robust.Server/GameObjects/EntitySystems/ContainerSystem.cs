using System;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;

namespace Robust.Server.GameObjects.EntitySystems
{
    public class ContainerSystem : SharedContainerSystem
    {
        public override void Initialize()
        {
            base.Initialize();

            SubscribeLocalEvent<ContainerManagerComponent, SyncContainerEvent>(HandleSyncContainer);
        }

        private void HandleSyncContainer(EntityUid uid, ContainerManagerComponent component, SyncContainerEvent args)
        {
            // TODO ShadowCommander: Make this ensure that the client gets all entities in this container.
        }
    }

    public class SyncContainerEvent : EntityEventArgs
    {
        public INetChannel ConnectedClient;

        public SyncContainerEvent(INetChannel connectedClient)
        {
            ConnectedClient = connectedClient;
        }
    }
}
