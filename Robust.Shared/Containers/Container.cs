using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Utility;

namespace Robust.Shared.Containers
{
    /// <summary>
    /// Default implementation for containers,
    /// cannot be inherited. If additional logic is needed,
    /// this logic should go on the systems that are holding this container.
    /// For example, inventory containers should be modified only through an inventory component.
    /// </summary>
    [UsedImplicitly]
    [SerializedType(ClassName)]
    public sealed class Container : BaseContainer
    {
        private const string ClassName = "Container";

        /// <summary>
        /// The generic container class uses a list of entities
        /// </summary>
        [DataField("ents")]
        private readonly List<IEntity> _containerList = new();

        /// <inheritdoc />
        public override IReadOnlyList<IEntity> ContainedEntities => _containerList;

        /// <inheritdoc />
        public override string ContainerType => ClassName;

        /// <inheritdoc />
        protected override void InternalInsert(IEntity toinsert)
        {
            _containerList.Add(toinsert);
            base.InternalInsert(toinsert);
        }

        /// <inheritdoc />
        protected override void InternalRemove(IEntity toremove)
        {
            _containerList.Remove(toremove);
            base.InternalRemove(toremove);
        }

        /// <inheritdoc />
        public override bool Contains(IEntity contained)
        {
            return _containerList.Contains(contained);
        }

        public override IEntity First()
        {
            DebugTools.Assert(_containerList.Count != 0, "Container is empty. Check container.Count before using this.");
            return _containerList[0];
        }

        public override IEntity Last()
        {
            DebugTools.Assert(_containerList.Count != 0, "Container is empty. Check container.Count before using this.");
            return _containerList[^1];
        }

        /// <inheritdoc />
        public override void Shutdown()
        {
            base.Shutdown();

            foreach (var entity in _containerList)
            {
                entity.Delete();
            }
        }
        public override bool TryGetFirst([NotNullWhen(true)] out IEntity? entity)
        {
            if (_containerList.Count == 0)
            {
                entity = null;
                return false;
            }

            entity = _containerList[0];
            return true;
        }

        public override bool TryGetLast([NotNullWhen(true)] out IEntity? entity)
        {
            if (_containerList.Count == 0)
            {
                entity = null;
                return false;
            }

            entity = _containerList[^1];
            return true;
        }
    }
}
