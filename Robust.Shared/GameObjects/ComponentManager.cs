using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using Robust.Shared.Exceptions;
using Robust.Shared.Physics;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;

namespace Robust.Shared.GameObjects
{
    /// <inheritdoc />
    public class ComponentManager : IComponentManager
    {
        [Dependency] private readonly IComponentFactory _componentFactory = default!;
        [Dependency] private readonly IComponentDependencyManager _componentDependencyManager = default!;

#if EXCEPTION_TOLERANCE
        [Dependency] private readonly IRuntimeLog _runtimeLog = default!;
#endif

        private const int TypeCapacity = 32;
        private const int ComponentCollectionCapacity = 1024;
        private const int EntityCapacity = 1024;
        private const int NetComponentCapacity = 8;

        private readonly Dictionary<EntityUid, Dictionary<uint, Component>> _netComponents
            = new(EntityCapacity);

        private readonly Dictionary<Type, Dictionary<EntityUid, Component>> _entTraitDict
            = new();

        private readonly HashSet<Component> _deleteSet = new(TypeCapacity);

        private UniqueIndexHkm<EntityUid, Component> _entCompIndex =
            new(ComponentCollectionCapacity);

        /// <inheritdoc />
        public event EventHandler<ComponentEventArgs>? ComponentAdded;

        /// <inheritdoc />
        public event EventHandler<ComponentEventArgs>? ComponentRemoved;

        /// <inheritdoc />
        public event EventHandler<ComponentEventArgs>? ComponentDeleted;

        private bool initialized;
        public void Initialize()
        {
            if (initialized)
                throw new InvalidOperationException("Already initialized.");

            initialized = true;

            FillComponentDict();
            _componentFactory.ComponentAdded += OnComponentAdded;
            _componentFactory.ComponentReferenceAdded += OnComponentReferenceAdded;
        }

        /// <inheritdoc />
        public void Clear()
        {
            _netComponents.Clear();
            _entTraitDict.Clear();
            _entCompIndex.Clear();
            _deleteSet.Clear();
            FillComponentDict();
        }

        public void Dispose()
        {
            _componentFactory.ComponentAdded -= OnComponentAdded;
            _componentFactory.ComponentReferenceAdded -= OnComponentReferenceAdded;
        }

        private void OnComponentAdded(IComponentRegistration obj)
        {
            _entTraitDict.Add(obj.Type, new Dictionary<EntityUid, Component>());
        }

        private void OnComponentReferenceAdded((IComponentRegistration, Type) obj)
        {
            _entTraitDict.Add(obj.Item2, new Dictionary<EntityUid, Component>());
        }

        #region Component Management

        /// <inheritdoc />
        public T AddComponent<T>(IEntity entity) where T : Component, new()
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            var newComponent = _componentFactory.GetComponent<T>();

            newComponent.Owner = entity;

            AddComponent(entity, newComponent);

            return newComponent;
        }

        /// <inheritdoc />
        public void AddComponent<T>(IEntity entity, T component, bool overwrite = false) where T : Component
        {
            if (entity == null || !entity.IsValid())
                throw new ArgumentException("Entity is not valid.", nameof(entity));

            if (component == null) throw new ArgumentNullException(nameof(component));

            if (component.Owner != entity) throw new InvalidOperationException("Component is not owned by entity.");

            var uid = entity.Uid;

            // get interface aliases for mapping
            var reg = _componentFactory.GetRegistration(component);

            // Check that there are no overlapping references.
            foreach (var type in reg.References)
            {
                var dict = _entTraitDict[type];
                if (!dict.TryGetValue(uid, out var duplicate))
                    continue;

                if (!overwrite && !duplicate.Deleted)
                    throw new InvalidOperationException(
                        $"Component reference type {type} already occupied by {duplicate}");

                // these two components are required on all entities and cannot be overwritten.
                if (duplicate is ITransformComponent || duplicate is IMetaDataComponent)
                    throw new InvalidOperationException("Tried to overwrite a protected component.");

                RemoveComponentImmediate(duplicate);
            }

            // add the component to the grid
            foreach (var type in reg.References)
            {
                _entTraitDict[type].Add(uid, component);
                _entCompIndex.Add(uid, component);
            }

            // add the component to the netId grid
            if (component.NetID != null)
            {
                // the main comp grid keeps this in sync
                var netId = component.NetID.Value;

                if (!_netComponents.TryGetValue(uid, out var netSet))
                {
                    netSet = new Dictionary<uint, Component>(NetComponentCapacity);
                    _netComponents.Add(uid, netSet);
                }
                netSet.Add(netId, component);

                // mark the component as dirty for networking
                component.Dirty();
            }

            ComponentAdded?.Invoke(this, new AddedComponentEventArgs(component, uid));

            _componentDependencyManager.OnComponentAdd(entity.Uid, component);

            component.OnAdd();

            if (!entity.Initialized && !entity.Initializing) return;

            component.Initialize();

            DebugTools.Assert(component.Initialized, "Component is not initialized after calling Initialize(). "
                                                     + "Did you forget to call base.Initialize() in an override?");

            if (entity.Initialized)
            {
                component.Running = true;
            }
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveComponent<T>(EntityUid uid)
        {
            RemoveComponent(uid, typeof(T));
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveComponent(EntityUid uid, Type type)
        {
            RemoveComponentDeferred((Component) GetComponent(uid, type), uid, false);
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveComponent(EntityUid uid, uint netId)
        {
            RemoveComponentDeferred((Component) GetComponent(uid, netId), uid, false);
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveComponent(EntityUid uid, IComponent component)
        {
            if (component == null) throw new ArgumentNullException(nameof(component));

            if (component.Owner == null || component.Owner.Uid != uid)
                throw new InvalidOperationException("Component is not owned by entity.");

            RemoveComponentDeferred((Component) component, uid, false);
        }

        private static IEnumerable<Component> InSafeOrder(IEnumerable<Component> comps, bool forCreation = false)
        {
            static int Sequence(IComponent x)
                => x switch
                {
                    ITransformComponent _ => 0,
                    IMetaDataComponent _ => 1,
                    IPhysBody _ => 2,
                    _ => int.MaxValue
                };

            return forCreation
                ? comps.OrderBy(Sequence)
                : comps.OrderByDescending(Sequence);
        }

        /// <inheritdoc />
        public void RemoveComponents(EntityUid uid)
        {
            foreach (var comp in InSafeOrder(_entCompIndex[uid]))
            {
                RemoveComponentDeferred(comp, uid, false);
            }
        }

        /// <inheritdoc />
        public void DisposeComponents(EntityUid uid)
        {
            foreach (var comp in InSafeOrder(_entCompIndex[uid]))
            {
                RemoveComponentDeferred(comp, uid, true);
            }

            // DisposeComponents means the entity is getting deleted.
            // Safe to wipe the entity out of the index.
            _entCompIndex.Remove(uid);
        }

        private void RemoveComponentDeferred(Component component, EntityUid uid, bool removeProtected)
        {
            if (component == null) throw new ArgumentNullException(nameof(component));

            if (component.Deleted) return;

#if EXCEPTION_TOLERANCE
            try
            {
#endif
                // these two components are required on all entities and cannot be removed normally.
                if (!removeProtected && (component is ITransformComponent || component is IMetaDataComponent))
                {
                    DebugTools.Assert("Tried to remove a protected component.");
                    return;
                }

                if (!_deleteSet.Add(component))
                {
                    // already deferred deletion
                    return;
                }

                component.Running = false;
                component.OnRemove();
                _componentDependencyManager.OnComponentRemove(uid, component);
                ComponentRemoved?.Invoke(this, new RemovedComponentEventArgs(component, uid));
#if EXCEPTION_TOLERANCE
            }
            catch (Exception e)
            {
                _runtimeLog.LogException(e,
                    $"RemoveComponentDeferred, owner={component.Owner}, type={component.GetType()}");
            }
#endif
        }

        private void RemoveComponentImmediate(Component component)
        {
            if (component == null) throw new ArgumentNullException(nameof(component));

            if (!component.Deleted)
            {
                // these two components are required on all entities and cannot be removed.
                if (component is ITransformComponent || component is IMetaDataComponent)
                {
                    DebugTools.Assert("Tried to remove a protected component.");
                    return;
                }

                component.Running = false;
                component.OnRemove(); // Sets delete
                ComponentRemoved?.Invoke(this, new RemovedComponentEventArgs(component, component.Owner.Uid));

            }

            DeleteComponent(component);
        }

        /// <inheritdoc />
        public void CullRemovedComponents()
        {
            foreach (var component in InSafeOrder(_deleteSet))
            {
                DeleteComponent(component);
            }

            _deleteSet.Clear();
        }

        private void DeleteComponent(Component component)
        {
            var reg = _componentFactory.GetRegistration(component.GetType());

            var entityUid = component.Owner.Uid;

            foreach (var refType in reg.References)
            {
                _entTraitDict[refType].Remove(entityUid);
            }

            // ReSharper disable once InvertIf
            if (component.NetID != null)
            {
                var netSet = _netComponents[entityUid];
                if (netSet.Count == 1)
                    _netComponents.Remove(entityUid);
                else
                    netSet.Remove(component.NetID.Value);

                component.Owner.Dirty();
            }

            _entCompIndex.Remove(entityUid, component);
            ComponentDeleted?.Invoke(this, new DeletedComponentEventArgs(component, entityUid));
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasComponent<T>(EntityUid uid)
        {
            return HasComponent(uid, typeof(T));
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasComponent(EntityUid uid, Type type)
        {
            var dict = _entTraitDict[type];
            return dict.TryGetValue(uid, out var comp) && !comp.Deleted;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasComponent(EntityUid uid, uint netId)
        {
            return _netComponents.TryGetValue(uid, out var netSet)
                   && netSet.ContainsKey(netId);
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T GetComponent<T>(EntityUid uid)
        {
            return (T) GetComponent(uid, typeof(T));
        }

        /// <inheritdoc />
        public IComponent GetComponent(EntityUid uid, Type type)
        {
            // ReSharper disable once InvertIf
            var dict = _entTraitDict[type];
            if (dict.TryGetValue(uid, out var comp))
            {
                if (!comp.Deleted)
                {
                    return comp;
                }
            }

            throw new KeyNotFoundException($"Entity {uid} does not have a component of type {type}");
        }

        /// <inheritdoc />
        public IComponent GetComponent(EntityUid uid, uint netId)
        {
            return _netComponents[uid][netId];
        }

        /// <inheritdoc />
        public bool TryGetComponent<T>(EntityUid uid, [NotNullWhen(true)] out T component)
        {
            if (TryGetComponent(uid, typeof(T), out var comp))
            {
                if (!comp.Deleted)
                {
                    component = (T) comp;
                    return true;
                }
            }

            component = default!;
            return false;
        }

        /// <inheritdoc />
        public bool TryGetComponent(EntityUid uid, Type type, [NotNullWhen(true)] out IComponent? component)
        {
            var dict = _entTraitDict[type];
            if (dict.TryGetValue(uid, out var comp))
            {
                if (!comp.Deleted)
                {
                    component = comp;
                    return true;
                }
            }

            component = null;
            return false;
        }

        /// <inheritdoc />
        public bool TryGetComponent(EntityUid uid, uint netId, [MaybeNullWhen(false)] out IComponent component)
        {
            if (_netComponents.TryGetValue(uid, out var netSet)
                && netSet.TryGetValue(netId, out var comp))
            {
                component = comp;
                return true;
            }

            component = default;
            return false;
        }

        /// <inheritdoc />
        public IEnumerable<IComponent> GetComponents(EntityUid uid)
        {
            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (Component comp in _entCompIndex[uid].ToArray())
            {
                if (comp.Deleted) continue;

                yield return comp;
            }
        }

        /// <inheritdoc />
        public IEnumerable<T> GetComponents<T>(EntityUid uid)
        {
            var comps = _entCompIndex[uid];
            foreach (var comp in comps)
            {
                if (comp.Deleted || comp is not T tComp) continue;

                yield return tComp;
            }
        }

        /// <inheritdoc />
        public IEnumerable<IComponent> GetNetComponents(EntityUid uid)
        {
            return _netComponents[uid].Values;
        }

        #region Join Functions

        /// <inheritdoc />
        public IEnumerable<T> EntityQuery<T>(bool includePaused = false)
        {
            var comps = _entTraitDict[typeof(T)];
            foreach (var comp in comps.Values)
            {
                if (comp.Deleted || !includePaused && comp.Paused) continue;

                yield return (T) (object) comp;
            }
        }

        /// <inheritdoc />
        public IEnumerable<(TComp1, TComp2)> EntityQuery<TComp1, TComp2>(bool includePaused = false)
            where TComp1 : IComponent
            where TComp2 : IComponent
        {
            // this would prob be faster if trait1 was a list (or an array of structs hue).
            var trait1 = _entTraitDict[typeof(TComp1)];
            var trait2 = _entTraitDict[typeof(TComp2)];

            // you really want trait1 to be the smaller set of components
            foreach (var kvComp in trait1)
            {
                var uid = kvComp.Key;

                if (!trait2.TryGetValue(uid, out var t2Comp) || t2Comp.Deleted || !includePaused && kvComp.Value.Paused)
                    continue;

                yield return ((TComp1) (object) kvComp.Value, (TComp2) (object) t2Comp);
            }
        }

        /// <inheritdoc />
        public IEnumerable<(TComp1, TComp2, TComp3)> EntityQuery<TComp1, TComp2, TComp3>(bool includePaused = false)
            where TComp1 : IComponent
            where TComp2 : IComponent
            where TComp3 : IComponent
        {
            var trait1 = _entTraitDict[typeof(TComp1)];
            var trait2 = _entTraitDict[typeof(TComp2)];
            var trait3 = _entTraitDict[typeof(TComp3)];

            foreach (var kvComp in trait1)
            {
                var uid = kvComp.Key;

                if (!trait2.TryGetValue(uid, out var t2Comp) || t2Comp.Deleted || !includePaused && kvComp.Value.Paused)
                    continue;

                if (!trait3.TryGetValue(uid, out var t3Comp) || t3Comp.Deleted)
                    continue;

                yield return ((TComp1) (object) kvComp.Value,
                    (TComp2) (object) t2Comp,
                    (TComp3) (object) t3Comp);
            }
        }

        /// <inheritdoc />
        public IEnumerable<(TComp1, TComp2, TComp3, TComp4)> EntityQuery<TComp1, TComp2, TComp3, TComp4>(bool includePaused = false)
            where TComp1 : IComponent
            where TComp2 : IComponent
            where TComp3 : IComponent
            where TComp4 : IComponent
        {
            var trait1 = _entTraitDict[typeof(TComp1)];
            var trait2 = _entTraitDict[typeof(TComp2)];
            var trait3 = _entTraitDict[typeof(TComp3)];
            var trait4 = _entTraitDict[typeof(TComp4)];

            foreach (var kvComp in trait1)
            {
                var uid = kvComp.Key;

                if (!trait2.TryGetValue(uid, out var t2Comp) || t2Comp.Deleted || !includePaused && kvComp.Value.Paused)
                    continue;

                if (!trait3.TryGetValue(uid, out var t3Comp) || t3Comp.Deleted)
                    continue;

                if (!trait4.TryGetValue(uid, out var t4Comp) || t4Comp.Deleted)
                    continue;

                yield return ((TComp1) (object) kvComp.Value,
                    (TComp2) (object) t2Comp,
                    (TComp3) (object) t3Comp,
                    (TComp4) (object) t4Comp);
            }
        }

        #endregion

        /// <inheritdoc />
        public IEnumerable<IComponent> GetAllComponents(Type type, bool includePaused = false)
        {
            var comps = _entTraitDict[type];
            foreach (var comp in comps.Values)
            {
                if (comp.Deleted || !includePaused && comp.Paused) continue;

                yield return comp;
            }
        }

        #endregion

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void FillComponentDict()
        {
            foreach (var refType in _componentFactory.GetAllRefTypes())
            {
                _entTraitDict.Add(refType, new Dictionary<EntityUid, Component>());
            }
        }
    }
}
