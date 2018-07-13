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
        public interface IScope : IDisposable, IServiceProvider
        {
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
        private Dictionary<Type, Func<ILifetime, object>> _registeredTypes = new Dictionary<Type, Func<ILifetime, object>>();

        // Lifetime management
        private ILifetime _lifetime;

        /// <summary>
        /// Creates a new instance of IoC Container
        /// </summary>
        public Container() => _lifetime = new ContainerLifetime(type => _registeredTypes[type]);

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
        /// Returns the object registered for the given type
        /// </summary>
        /// <param name="type">Type as registered with the container</param>
        /// <returns>Instance of the registered type</returns>
        public object GetService(Type type) => _lifetime.GetService(type);

        /// <summary>
        /// Creates a new scope
        /// </summary>
        /// <returns>Scope object</returns>
        public IScope CreateScope() => new ScopeLifetime(type => _registeredTypes[type], _lifetime);

        public void Dispose() => _lifetime.Dispose();

        #region Lifetime management
        interface ILifetime : IScope
        {
            object GetServiceAsSingleton(Type type, Func<ILifetime, object> factory);

            object GetServicePerScope(Type type, Func<ILifetime, object> factory);
        }

        abstract class BaseContainer : ILifetime
        {
            // Function to get factory registered for type
            private Func<Type, Func<ILifetime, object>> _getFactory;

            // Instance cache
            protected ConcurrentDictionary<Type, object> _instanceCache = new ConcurrentDictionary<Type, object>();

            public BaseContainer(Func<Type, Func<ILifetime, object>> getFactory)
            {
                _getFactory = getFactory;
            }

            public object GetService(Type type) => _getFactory(type)(this);

            public abstract object GetServiceAsSingleton(Type type, Func<ILifetime, object> factory);

            public abstract object GetServicePerScope(Type type, Func<ILifetime, object> factory);

            public void Dispose()
            {
                foreach (var obj in _instanceCache.Values)
                    (obj as IDisposable)?.Dispose();
            }
        }

        class ContainerLifetime : BaseContainer
        {
            public ContainerLifetime(Func<Type, Func<ILifetime, object>> getFactory)
                : base(getFactory)
            { }

            public override object GetServiceAsSingleton(Type type, Func<ILifetime, object> factory) 
                => _instanceCache.GetOrAdd(type, _ => factory(this));

            public override object GetServicePerScope(Type type, Func<ILifetime, object> factory)
                => factory(this);
        }

        class ScopeLifetime : BaseContainer
        {
            private ILifetime _singletonLifetime;

            public ScopeLifetime(Func<Type, Func<ILifetime, object>> getFactory, ILifetime singletonLifetime)
                : base(getFactory)
            {
                _singletonLifetime = singletonLifetime;
            }

            public override object GetServiceAsSingleton(Type type, Func<ILifetime, object> factory)
                => _singletonLifetime.GetServiceAsSingleton(type, factory);

            public override object GetServicePerScope(Type type, Func<ILifetime, object> factory)
                => _instanceCache.GetOrAdd(type, _ => factory(_singletonLifetime));
        }
        #endregion

        #region Container items
        // Compiles a lambda that calls the given type's first constructor resolving arguments
        private static Func<ILifetime, object> FactoryFromType(Type itemType)
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
            var arg = Expression.Parameter(typeof(ILifetime));
            return (Func<ILifetime, object>)Expression.Lambda(
                Expression.New(constructor, constructor.GetParameters().Select(
                    param =>
                    {
                        var resolve = new Func<ILifetime, object>(
                            lifetime => lifetime.GetService(param.ParameterType));
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
            Func<ILifetime, object> _factory;
            private Type _itemType;

            public RegisteredType(Container container, Func<ILifetime, object> factory, Type itemType)
            {
                _container = container;
                _factory = factory;
                _itemType = itemType;
            }

            public void AsSingleton()
                => _container._registeredTypes[_itemType] =
                    lifetime => lifetime.GetServiceAsSingleton(_itemType, _factory);

            public void PerScope() 
                => _container._registeredTypes[_itemType] =
                    lifetime => lifetime.GetServicePerScope(_itemType, _factory);
        }
        #endregion
    }

    /// <summary>
    /// Extension methods for Container.IScope
    /// </summary>
    static class IScopeExtensions
    {
        /// <summary>
        /// Returns an implementation of the specified interface
        /// </summary>
        /// <typeparam name="T">Interface type</typeparam>
        /// <returns>Object implementing the interface</returns>
        public static T Resolve<T>(this Container.IScope scope) => (T)scope.GetService(typeof(T));
    }
}