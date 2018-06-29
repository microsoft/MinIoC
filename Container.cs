// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
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
            return _registeredTypes[typeof(T)] = ContainerItem.FromType(type);
        }

        /// <summary>
        /// Registers a factory function which will be called to resolve the specified interface
        /// </summary>
        /// <typeparam name="T">Interface to register</typeparam>
        /// <param name="factory">Factory method</param>
        /// <returns>IRegisteredType object</returns>
        public static IRegisteredType Register<T>(Func<T> factory)
        {
            return _registeredTypes[typeof(T)] = ContainerItem.FromFactory<T>(factory);
        }

        /// <summary>
        /// Returns an implementation of the specified interface
        /// </summary>
        /// <typeparam name="T">Interface type</typeparam>
        /// <returns>Object implementing the interface</returns>
        public static T Resolve<T>()
        {
            // Call internal Resolve with the scope null object
            // Per-scope objects are creates as instance objects in this case
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
            object GetCachedInstance(Type type, Func<IScopeCache, object> factory);
        }

        // Scope implementation
        class Scope : IScopeCache
        {
            public static IScopeCache Null { get; } = new NullScope();
            private ConcurrentDictionary<Type, object> _instanceCache = new ConcurrentDictionary<Type, object>();

            // Scope.Resolve invokes Container.Resolve passing in the scope instance
            public T Resolve<T>() => Container.Resolve<T>(this);

            // Get cached instance for the given type (creates a new instance if not cached)
            public object GetCachedInstance(Type type, Func<IScopeCache, object> factory)
                =>  _instanceCache.GetOrAdd(type, _ => factory(this));

            // No need to free resources, we use IDisposable to enable "using" syntax
            public void Dispose()
            {
            }
        }

        // Null implementation of IScopeCache - objects never get cached
        class NullScope : IScopeCache
        {
            // Resolve should never be called on NullScope
            public T Resolve<T>() => throw new NotImplementedException();

            // Directly call factory instead of caching
            public object GetCachedInstance(Type type, Func<IScopeCache, object> factory) => factory(this);

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
            public Func<IScopeCache, object> Resolve { get; private set; }

            private ContainerItem(Type itemType, Func<IScopeCache, object> factory)
            {
                _itemType = itemType;
                Resolve = factory;
            }

            public static ContainerItem FromFactory<T>(Func<T> factory)
            {
                return new ContainerItem(typeof(T), scopeCache => factory());
            }

            public static ContainerItem FromType(Type itemType)
            {
                return new ContainerItem(itemType, FactoryFromType(itemType));
            }

            public void AsSingleton()
            {
                // Decorate factory with singleton resolution
                Resolve = SingletonDecorator(_itemType, Resolve);
            }

            public void PerScope()
            {
                // Decorate factory with per-scope resolution
                Resolve = PerScopeDecorator(_itemType, Resolve);
            }

            // Compiles a lambda that calls the given type's first constructor resolving arguments
            private static Func<IScopeCache, object> FactoryFromType(Type itemType)
            {
                // Get first constructor for the type
                var constructors = itemType.GetConstructors();
                if (constructors.Length == 0)
                {
                    // If no public constructor found, search for an internal constructor
                    constructors = itemType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic);
                }
                var constructor = constructors.First();

                // Compile constructor call as a lambda expression
                var arg = Expression.Parameter(typeof(IScopeCache));
                return (Func<IScopeCache, object>)Expression.Lambda(
                    Expression.New(constructor, constructor.GetParameters().Select(
                        param =>
                        {
                            var resolve = new Func<IScopeCache, object>(
                                scopeCache => _registeredTypes[param.ParameterType].Resolve(scopeCache));
                            return Expression.Convert(
                                Expression.Call(Expression.Constant(resolve.Target), resolve.Method, arg),
                                param.ParameterType);
                        })),
                    arg).Compile();
            }

            // Singleton decorates the factory with singleton resolution
            private static Func<IScopeCache, object> SingletonDecorator(Type type, Func<IScopeCache, object> factory)
            {
                var _instance = new Lazy<object>(() => factory(Scope.Null));
                return _ => _instance.Value;
            }

            // Per-scope decorates the factory with single instance per scope resolution
            private static Func<IScopeCache, object> PerScopeDecorator(Type itemType, Func<IScopeCache, object> factory)
            {
                return scopeCache => scopeCache.GetCachedInstance(itemType, factory);
            }
        }
        #endregion
    }
}
