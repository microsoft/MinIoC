using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace Microsoft.MinIoC
{
    /// <summary>
    /// Inversion of control container handles dependency injection for registered types
    /// </summary>
    static class Container
    {
        // Map of registered types
        static private Dictionary<Type, ContainerItem> _registeredTypes = new Dictionary<Type, ContainerItem>();

        /// <summary>
        /// Registers an implementation type for the specified interface
        /// </summary>
        /// <typeparam name="T">Interface to register</typeparam>
        /// <param name="type">Implementing type</param>
        /// <returns>IRegisteredType object</returns>
        public static IRegisteredType Register<T>(Type type)
        {
            return _registeredTypes[typeof(T)] = new ContainerItem(type, new ReflectionConstructionStrategy(type));
        }

        /// <summary>
        /// Registers a factory function which will be called to resolve the specified interface
        /// </summary>
        /// <typeparam name="T">Interface to register</typeparam>
        /// <param name="factory">Factory method</param>
        /// <returns>IRegisteredType object</returns>
        public static IRegisteredType Register<T>(Func<T> factory)
        {
            return _registeredTypes[typeof(T)] = new ContainerItem(typeof(T), new FactoryConstructionStrategy<T>(factory));
        }

        /// <summary>
        /// Returns an implementation of the specified interface
        /// </summary>
        /// <typeparam name="T">Interface type</typeparam>
        /// <returns>Object implementing the interface</returns>
        public static T Resolve<T>()
        {
            // Call internal Resolve with the scope null object
            // Per-context objects are creates as instance objects in this case
            return Resolve<T>(Scope.Null);
        }

        /// <summary>
        /// Creates a new scope
        /// </summary>
        /// <returns>Scope object</returns>
        public static IScope CreateScope() => new Scope();

        // Resolve the given type within the given scope
        private static T Resolve<T>(IScopeCache scope) => (T)_registeredTypes[typeof(T)].Resolve(scope);

        #region Scope management to enable per-scope objects
        /// <summary>
        /// Represents a scope in which per-scope objects are instantiated a single time
        /// </summary>
        public interface IScope : IDisposable
        {
            /// <summary>
            /// Returns an implementation of the specified interface
            /// </summary>
            /// <typeparam name="T">Interface type</typeparam>
            /// <returns>Object implementing the interface</returns>
            T Resolve<T>();
        }

        // Object cache where per-scope objects are stored
        interface IScopeCache : IScope
        {
            void CacheInstance(Type type, object instanceToCache);
            object GetCachedInstance(Type type);
        }

        // Scope implementation
        class Scope : IScopeCache
        {
            private Dictionary<Type, object> _instanceCache = new Dictionary<Type, object>();
            public static IScopeCache Null = new NullScope();

            // Scope.Resolve invokes Container.Resolve passing in the scope instance
            public T Resolve<T>() => Container.Resolve<T>(this);

            // Cache instance for a given type
            public void CacheInstance(Type type, object instanceToCache)
            {
                // There should never be an instance already cached for this type
                Debug.Assert(!_instanceCache.ContainsKey(type));

                _instanceCache[type] = instanceToCache;
            }

            // Get cached instance for the given type or null if not cached
            public object GetCachedInstance(Type type)
            {
                object result;
                _instanceCache.TryGetValue(type, out result);
                return result;
            }

            // No need to free resources, we use IDisposable to enable using synstax
            public void Dispose()
            {
            }
        }

        // Null implementation of IScopeCache - objects never get cached
        class NullScope : IScopeCache
        {
            // Resolve should never be called on NullScope
            public T Resolve<T>() => throw new NotImplementedException();

            public object GetCachedInstance(Type type) => null;

            public void CacheInstance(Type type, object instanceToCache)
            {
            }

            public void Dispose()
            {
            }
        }
        #endregion

        #region Container items
        /// <summary>
        /// IRegisteredType is return by Container.Register and allows further configuration for the registration
        /// </summary>
        public interface IRegisteredType
        {
            /// <summary>
            /// Make registered type a singleton
            /// </summary>
            void AsSingleton();

            /// <summary>
            /// Make registered type a per-scope type (single instance within a Scope)
            /// </summary>
            void PerScope();
        }

        // Container item
        class ContainerItem : IRegisteredType
        {
            private Type _itemType;
            private IConstructionStrategy _constructionStrategy;
            private IResolutionStrategy _resolutionStrategy;

            public ContainerItem(Type itemType, IConstructionStrategy constructionStrategy)
            {
                _itemType = itemType;
                _constructionStrategy = constructionStrategy;

                // Default strategy is one instance per call
                _resolutionStrategy = new InstanceResolutionStrategy();
            }

            public void AsSingleton()
            {
                // Change resolution strategy to singleton
                _resolutionStrategy = new SingletonResolutionStrategy();
            }

            public void PerScope()
            {
                // Change resolution strategy to per-scope
                _resolutionStrategy = new PerScopeResolutionStrategy();
            }

            public object Resolve(IScopeCache scope)
            {
                // Resolve forwards the call to the resolution strategy
                return _resolutionStrategy.Resolve(scope, _itemType, _constructionStrategy);
            }
        }
        #endregion

        #region Construction strategies
        // Construction strategies implement the object creation mechanisms
        interface IConstructionStrategy
        {
            object MakeInstance(IScopeCache scope);
        }

        // Reflection construction strategy uses reflection to invoke the first constructor and recursively resolves arguments
        class ReflectionConstructionStrategy : IConstructionStrategy
        {
            private Type _itemType;

            public ReflectionConstructionStrategy(Type itemType)
            {
                _itemType = itemType;
            }

            public object MakeInstance(IScopeCache scope)
            {
                // Get first constructor for the type
                var constructors = _itemType.GetConstructors();
                if (constructors.Length == 0)
                {
                    // If no public constructor found, search for an internal constructor
                    constructors = _itemType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic);
                }
                var constructor = constructors.First();

                // Prepare constructor arguments
                List<object> arguments = new List<object>();
                foreach (var parameter in constructor.GetParameters())
                {
                    // Recursively resolve dependencies for this type
                    arguments.Add(_registeredTypes[parameter.ParameterType].Resolve(scope));
                }

                // Call constructor and return object
                return constructor.Invoke(arguments.ToArray());
            }
        }

        // Factory construction strategy uses a passed in factory function to construct the object
        class FactoryConstructionStrategy<T> : IConstructionStrategy
        {
            private Func<T> _factory;

            public FactoryConstructionStrategy(Func<T> factory)
            {
                _factory = factory;
            }

            public object MakeInstance(IScopeCache scope) => _factory();
        }
        #endregion

        #region Resolution strategies
        // Resolution strategies implement the various object lifetimes (instance, scope, or singleton)
        interface IResolutionStrategy
        {
            object Resolve(IScopeCache scope, Type type, IConstructionStrategy factory);
        }

        // Instance resolution strategy creates a new instance on each call
        class InstanceResolutionStrategy : IResolutionStrategy
        {
            public object Resolve(IScopeCache scope, Type type, IConstructionStrategy factory)
            {
                return factory.MakeInstance(scope);
            }
        }

        // Singleton resolution strategy creates a single instance for the whole app domain
        class SingletonResolutionStrategy : IResolutionStrategy
        {
            private object _syncRoot = new object();
            private object _instance { get; set; }

            public object Resolve(IScopeCache scope, Type type, IConstructionStrategy factory)
            {
                if (_instance != null) return _instance;

                lock (_syncRoot)
                {
                    if (_instance != null) return _instance;
                    
                    _instance = factory.MakeInstance(scope);
                }

                return _instance;
            }
        }

        // Per-scope resolution strategy creates a single instance per scope
        class PerScopeResolutionStrategy : IResolutionStrategy
        {
            private object _syncRoot = new object();

            public object Resolve(IScopeCache scope, Type type, IConstructionStrategy factory)
            {
                object result = scope.GetCachedInstance(type);

                if (result != null) return result;

                // Lock only if we don't have a cached object, we need to ensure we
                // only create and cache the object once
                lock (_syncRoot)
                {
                    // Check again if another thread cached an instance
                    result = scope.GetCachedInstance(type);
                    if (result != null) return result;

                    result = factory.MakeInstance(scope);
                    scope.CacheInstance(type, result);
                }

                return result;
            }
        }
        #endregion
    }
}