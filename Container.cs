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
    class Container : Container.IScope
    {
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

        // Map of registered types
        private Dictionary<Type, ContainerItem> _registeredTypes = new Dictionary<Type, ContainerItem>();
        
        // Instance cache
        private ConcurrentDictionary<Type, object> _instanceCache = new ConcurrentDictionary<Type, object>();

        // Parent container
        private Container _parent = null;

        /// <summary>
        /// Registers an implementation type for the specified interface
        /// </summary>
        /// <typeparam name="T">Interface to register</typeparam>
        /// <param name="type">Implementing type</param>
        /// <returns>IRegisteredType object</returns>
        public IRegisteredType Register<T>(Type type)
        {
            return _registeredTypes[typeof(T)] = ContainerItem.FromType(type);
        }

        /// <summary>
        /// Registers a factory function which will be called to resolve the specified interface
        /// </summary>
        /// <typeparam name="T">Interface to register</typeparam>
        /// <param name="factory">Factory method</param>
        /// <returns>IRegisteredType object</returns>
        public IRegisteredType Register<T>(Func<T> factory)
        {
            return _registeredTypes[typeof(T)] = ContainerItem.FromFactory<T>(factory);
        }

        /// <summary>
        /// Returns an implementation of the specified interface
        /// </summary>
        /// <typeparam name="T">Interface type</typeparam>
        /// <returns>Object implementing the interface</returns>
        public T Resolve<T>() => (T)Resolve(typeof(T));

        // Resolve the given type
        private object Resolve(Type type) => _registeredTypes[type].Resolve(this);

        /// <summary>
        /// Creates a new scope
        /// </summary>
        /// <returns>Scope object</returns>
        public IScope CreateScope()
        {
            // Create a new container
            var scope = new Container() { _parent = this };

            // Clone registered types
            foreach (var kv in _registeredTypes)
            {
                scope._registeredTypes[kv.Key] = kv.Value.Clone(kv.Key);
            }

            return scope;
        }

        // Call Dispose() on cached IDisposable objects
        public void Dispose()
        {
            foreach (var obj in _instanceCache.Values)
                (obj as IDisposable)?.Dispose();
        }

        #region Container items
        // Container item
        class ContainerItem : IRegisteredType
        {
            private Type _itemType;
            public Func<Container, object> Resolve { get; set; }
            public Func<Type, ContainerItem> Clone { get; set; }

            private ContainerItem(Type itemType, Func<Container, object> factory)
            {
                _itemType = itemType;
                Resolve = factory;

                // By default Clone just returns this object
                Clone = _ => this;
            }

            public static ContainerItem FromFactory<T>(Func<T> factory)
            {
                return new ContainerItem(typeof(T), _ => factory());
            }

            public static ContainerItem FromType(Type itemType)
            {
                return new ContainerItem(itemType, FactoryFromType(itemType));
            }

            public void AsSingleton()
            {
                // Decorate factory with singleton resolution
                Resolve = CacheDecorator(_itemType, Resolve);

                // Clone returns a new instance calling Resolve on parent container
                Clone = type => new ContainerItem(_itemType, container => container._parent.Resolve(type));
            }

            public void PerScope()
            {
                // Clone returns a new instance decorated with caching logic
                Clone = _ => new ContainerItem(_itemType, CacheDecorator(_itemType, Resolve));
            }

            // Compiles a lambda that calls the given type's first constructor resolving arguments
            private static Func<Container, object> FactoryFromType(Type itemType)
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
                var arg = Expression.Parameter(typeof(Container));
                return (Func<Container, object>)Expression.Lambda(
                    Expression.New(constructor, constructor.GetParameters().Select(
                        param =>
                        {
                            var resolve = new Func<Container, object>(
                                container => container.Resolve(param.ParameterType));
                            return Expression.Convert(
                                Expression.Call(Expression.Constant(resolve.Target), resolve.Method, arg),
                                param.ParameterType);
                        })),
                    arg).Compile();
            }

            // Cache decorator adds caching to the factory
            private static Func<Container, object> CacheDecorator(Type type, Func<Container, object> factory)
            {
                return container => container._instanceCache.GetOrAdd(type, factory(container));
            }
        }
        #endregion
    }
}