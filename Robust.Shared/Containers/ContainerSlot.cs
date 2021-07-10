using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Utility;
using Robust.Shared.ViewVariables;

namespace Robust.Shared.Containers
{
    [UsedImplicitly]
    [SerializedType(ClassName)]
    public class ContainerSlot : BaseContainer
    {
        private const string ClassName = "ContainerSlot";

        /// <inheritdoc />
        public override IReadOnlyList<IEntity> ContainedEntities
        {
            get
            {
                if (ContainedEntity == null)
                    return Array.Empty<IEntity>();

                // Cast to handle nullability.
                return (IEntity[]) _containedEntityArray!;
            }
        }

        [ViewVariables]
        [DataField("ent")]
        public IEntity? ContainedEntity
        {
            get => _containedEntity;
            private set
            {
                _containedEntity = value;
                _containedEntityArray[0] = value;
            }
        }

        private IEntity? _containedEntity;
        // Used by ContainedEntities to avoid allocating.
        private readonly IEntity?[] _containedEntityArray = new IEntity[1];

        /// <inheritdoc />
        public override string ContainerType => ClassName;

        /// <inheritdoc />
        public override bool CanInsert(IEntity toinsert)
        {
            if (ContainedEntity != null)
                return false;
            return base.CanInsert(toinsert);
        }

        /// <inheritdoc />
        public override bool Contains(IEntity contained)
        {
            if (contained == ContainedEntity)
                return true;
            return false;
        }

        public override IEntity First()
        {
            DebugTools.Assert(_containedEntity != null, "Container is empty. Check container.Count before using this.");
            return _containedEntity!;
        }

        public override IEntity Last()
        {
            DebugTools.Assert(_containedEntity != null, "Container is empty. Check container.Count before using this.");
            return _containedEntity!;
        }

        public override bool TryGetFirst([NotNullWhen(true)] out IEntity? entity)
        {
            entity = _containedEntity;
            if (entity == null)
                return false;
            return true;
        }

        public override bool TryGetLast([NotNullWhen(true)] out IEntity? entity)
        {
            entity = _containedEntity;
            if (entity == null)
                return false;
            return true;
        }

        /// <inheritdoc />
        protected override void InternalInsert(IEntity toinsert)
        {
            ContainedEntity = toinsert;
            base.InternalInsert(toinsert);
        }

        /// <inheritdoc />
        protected override void InternalRemove(IEntity toremove)
        {
            ContainedEntity = null;
            base.InternalRemove(toremove);
        }

        /// <inheritdoc />
        public override void Shutdown()
        {
            base.Shutdown();

            ContainedEntity?.Delete();
        }
    }
}
