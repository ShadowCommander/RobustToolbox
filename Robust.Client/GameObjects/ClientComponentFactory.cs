using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics;

namespace Robust.Client.GameObjects
{
    public class ClientComponentFactory : ComponentFactory
    {
        public ClientComponentFactory()
        {
            // Required for the engine to work
            Register<MetaDataComponent>();
            RegisterReference<MetaDataComponent, IMetaDataComponent>();

            // Required for the engine to work
            Register<TransformComponent>();
            RegisterReference<TransformComponent, ITransformComponent>();

            Register<MapComponent>();
            RegisterReference<MapComponent, IMapComponent>();

            Register<MapGridComponent>();
            RegisterReference<MapGridComponent, IMapGridComponent>();

            Register<PhysicsComponent>();
            RegisterReference<PhysicsComponent, IPhysBody>();

            Register<CollisionWakeComponent>();

            Register<ContainerManagerComponent>();
            RegisterReference<ContainerManagerComponent, IContainerManager>();

            RegisterIgnore("KeyBindingInput");

            Register<InputComponent>();

            Register<SpriteComponent>();
            RegisterReference<SpriteComponent, SharedSpriteComponent>();
            RegisterReference<SpriteComponent, ISpriteComponent>();

            Register<ClientOccluderComponent>();
            RegisterReference<ClientOccluderComponent, OccluderComponent>();

            Register<EyeComponent>();
            RegisterReference<EyeComponent, SharedEyeComponent>();

            Register<AppearanceComponent>();
            RegisterReference<AppearanceComponent, SharedAppearanceComponent>();

            Register<AppearanceTestComponent>();
            Register<SnapGridComponent>();

            Register<ClientUserInterfaceComponent>();
            RegisterReference<ClientUserInterfaceComponent, SharedUserInterfaceComponent>();

            Register<AnimationPlayerComponent>();

            Register<TimerComponent>();

#if DEBUG
            Register<DebugExceptionOnAddComponent>();
            Register<DebugExceptionInitializeComponent>();
            Register<DebugExceptionStartupComponent>();
#endif

        }
    }
}
