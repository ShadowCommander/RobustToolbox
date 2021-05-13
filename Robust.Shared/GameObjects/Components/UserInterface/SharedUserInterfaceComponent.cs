using System;
using Robust.Shared.IoC;
using Robust.Shared.Reflection;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Robust.Shared.GameObjects
{
    public abstract class SharedUserInterfaceComponent : Component
    {
        public sealed override string Name => "UserInterface";
        public sealed override uint? NetID => NetIDs.USERINTERFACE;

        [DataDefinition]
        public sealed class PrototypeData : ISerializationHooks
        {
            public object UiKey { get; set; } = default!;

            [DataField("key", readOnly: true, required: true)]
            private string _uiKeyRaw = default!;

            [DataField("type", readOnly: true, required: true)]
            public string ClientType { get; set; } = default!;

            void ISerializationHooks.AfterDeserialization()
            {
                var reflectionManager = IoCManager.Resolve<IReflectionManager>();

                if (reflectionManager.TryParseEnumReference(_uiKeyRaw, out var @enum))
                {
                    UiKey = @enum;
                    return;
                }

                UiKey = _uiKeyRaw;
            }
        }

        [NetSerializable, Serializable]
        protected sealed class BoundInterfaceMessageWrapMessage : ComponentMessage
        {
            public readonly BoundUserInterfaceMessage Message;
            public readonly object UiKey;

            public BoundInterfaceMessageWrapMessage(BoundUserInterfaceMessage message, object uiKey)
            {
                Directed = true;
                Message = message;
                UiKey = uiKey;
            }

            public override string ToString()
            {
                return $"{nameof(BoundInterfaceMessageWrapMessage)}: {Message}";
            }
        }
    }

    [NetSerializable, Serializable]
    public abstract class BoundUserInterfaceState
    {
    }


    [NetSerializable, Serializable]
    public class BoundUserInterfaceMessage
    {
    }

    [NetSerializable, Serializable]
    internal sealed class UpdateBoundStateMessage : BoundUserInterfaceMessage
    {
        public readonly BoundUserInterfaceState State;

        public UpdateBoundStateMessage(BoundUserInterfaceState state)
        {
            State = state;
        }
    }

    [NetSerializable, Serializable]
    internal sealed class OpenBoundInterfaceMessage : BoundUserInterfaceMessage
    {
    }

    [NetSerializable, Serializable]
    internal sealed class CloseBoundInterfaceMessage : BoundUserInterfaceMessage
    {
    }
}
