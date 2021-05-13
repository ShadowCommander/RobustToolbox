using System.Collections.Generic;
using Robust.Shared.GameObjects;

namespace Robust.Shared.Physics
{
    internal class IslandSolveMessage : EntityEventArgs
    {
        public List<IPhysBody> Bodies { get; }

        public IslandSolveMessage(List<IPhysBody> bodies)
        {
            Bodies = bodies;
        }
    }
}
