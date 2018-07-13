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
        #region Public interfaces
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
        #endregion

        // Map of registered types
        private Dictionary<Type, Func<Container, object>> _registeredTypes = new Dictionary<Type, Func<Container, object>>();
        
        // Instance cache
        private ConcurrentDictionary<Type, object> _instanceCache = new ConcurrentDictionary<Type, object>();

        /// <summary>
        /// Registers an implementation type for the specified interface
        /// </summary>
        /// <typeparam name="T">Interface to register</typeparam>
        /// <param name="type">Implementing type</param>
        /// <returns>IRegisteredType object</returns>
        public IRegisteredType Register<T>(Type type)
            => new RegisteredType(this, _registeredTypes[typeof(T)] = FactoryFromType(type), typeof(T));

        /// <summary>
        /// Registers a factory function which will be called to resolve the specified interface
        /// </summary>
        /// <typeparam name="T">Interface to register</typeparam>
        /// <param name="factory">Factory method</param>
        /// <returns>IRegisteredType object</returns>
        public IRegisteredType Register<T>(Func<T> factory)
            => new RegisteredType(this, _registeredTypes[typeof(T)] = _ => factory(), typeof(T));

        /// <summary>
        /// Returns an implementation of the specified interface
        /// </summary>
        /// <typeparam name="T">Interface type</typeparam>
        /// <returns>Object implementing the interface</returns>
        public T Resolve<T>() => (T)Resolve(typeof(T));

        // Resolve the given type
        private object Resolve(Type type) => _registeredTypes[type](this);

        // Singleton resolution strategy
        protected virtual object ResolveSingleton(Type type, Func<Container, object> factory)
            => _instanceCache.GetOrAdd(type, _ => factory(this));

        // Per-scope resolution strategy
        protected virtual object ResolvePerScope(Type type, Func<Container, object> factory)
            => factory(this);

        // Scope is a Container
        class Scope : Container
        {
            private Container _parent;

            public Scope(Container parent)
            {
                _parent = parent;
                _registeredTypes = _parent._registeredTypes;
            }
            
            // Delegate singleton resolution to parent scope
            protected override object ResolveSingleton(Type type, Func<Container, object> factory)
                => _parent.ResolveSingleton(type, factory);

            // Cache per-scope instances
            protected override object ResolvePerScope(Type type, Func<Container, object> factory)
                => _instanceCache.GetOrAdd(type, _ => factory(this));
        }

        /// <summary>
        /// Creates a new scope
        /// </summary>
        /// <returns>Scope object</returns>
        public IScope CreateScope() => new Scope(this);
        
        public void Dispose()
        {
            foreach (var obj in _instanceCache.Values)
                (obj as IDisposable)?.Dispose();
        }

        #region Container items
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

        // RegisteredType is supposed to be a short lived object tying an item to its container
        // and allowing users to mark it as a singleton or per-scope item
        class RegisteredType : IRegisteredType
        {
            private Container _container;
            Func<Container, object> _factory;
            private Type _itemType;

            public RegisteredType(Container container, Func<Container, object> factory, Type itemType)
            {
                _container = container;
                _factory = factory;
                _itemType = itemType;
            }

            public void AsSingleton()
                => _container._registeredTypes[_itemType] =
                    container => container.ResolveSingleton(_itemType, _factory);

            public void PerScope() 
                => _container._registeredTypes[_itemType] = 
                    container => container.ResolvePerScope(_itemType, _factory);
        }
        #endregion
    }
}