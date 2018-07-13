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
    public class Container : Container.IScope
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
        private ContainerLifetime _lifetime;

        /// <summary>
        /// Creates a new instance of IoC Container
        /// </summary>
        public Container() => _lifetime = new ContainerLifetime(t => _registeredTypes[t]);

        /// <summary>
        /// Registers an implementation type for the specified interface
        /// </summary>
        /// <typeparam name="T">Interface to register</typeparam>
        /// <param name="type">Implementing type</param>
        /// <returns>IRegisteredType object</returns>
        public IRegisteredType Register<T>(Type type)
            => new RegisteredType<T>((t, f) => _registeredTypes[t] = f, FactoryFromType(type));

        /// <summary>
        /// Registers a factory function which will be called to resolve the specified interface
        /// </summary>
        /// <typeparam name="T">Interface to register</typeparam>
        /// <param name="factory">Factory method</param>
        /// <returns>IRegisteredType object</returns>
        public IRegisteredType Register<T>(Func<T> factory)
            => new RegisteredType<T>((t, f) => _registeredTypes[t] = f,  _ => factory());
        
        /// <summary>
        /// Returns the object registered for the given type
        /// </summary>
        /// <param name="type">Type as registered with the container</param>
        /// <returns>Instance of the registered type</returns>
        public object GetService(Type type) => _registeredTypes[type](_lifetime);

        /// <summary>
        /// Creates a new scope
        /// </summary>
        /// <returns>Scope object</returns>
        public IScope CreateScope() => new ScopeLifetime(_lifetime);

        public void Dispose() => _lifetime.Dispose();
        
        #region Lifetime management
        // ILifetime management adds resolution strategies to an IScope
        interface ILifetime : IScope
        {
            object GetServiceAsSingleton(Type type, Func<ILifetime, object> factory);

            object GetServicePerScope(Type type, Func<ILifetime, object> factory);
        }

        // Base lifetime provides common caching logic
        abstract class BaseLifetime
        {
            // Instance cache
            private ConcurrentDictionary<Type, object> _instanceCache = new ConcurrentDictionary<Type, object>();

            // Get from cache or create and cache object
            protected object GetCached(Type type, Func<ILifetime, object> factory, ILifetime lifetime)
                => _instanceCache.GetOrAdd(type, _ => factory(lifetime));

            public void Dispose()
            {
                foreach (var obj in _instanceCache.Values)
                    (obj as IDisposable)?.Dispose();
            }
        }

        // Container lifetime management
        class ContainerLifetime : BaseLifetime, ILifetime
        {
            private Func<Type, Func<ILifetime, object>> _getFactory;

            public ContainerLifetime(Func<Type, Func<ILifetime, object>> getFactory) => _getFactory = getFactory;

            public Func<ILifetime, object> GetFactory(Type type) => _getFactory(type);

            // Calls given _getFactory function
            public object GetService(Type type) => GetFactory(type)(this);

            // Singletons get cached per container
            public object GetServiceAsSingleton(Type type, Func<ILifetime, object> factory)
                => GetCached(type, factory, this);

            // At container level, per-scope items are not cached
            public object GetServicePerScope(Type type, Func<ILifetime, object> factory)
                => factory(this);
        }

        // Per-scope lifetime management
        class ScopeLifetime : BaseLifetime, ILifetime
        {
            // Singletons come from parent container's lifetime
            private ContainerLifetime _parentContainer;

            public ScopeLifetime(ContainerLifetime parentContainer) => _parentContainer = parentContainer;

            // Calls given _getFactory function
            public object GetService(Type type) => _parentContainer.GetFactory(type)(this);

            // Singleton resolution is delegated to given lifetime
            public object GetServiceAsSingleton(Type type, Func<ILifetime, object> factory)
                => _parentContainer.GetServiceAsSingleton(type, factory);

            // Per-scope objects get cached
            public object GetServicePerScope(Type type, Func<ILifetime, object> factory)
                => GetCached(type, factory, this);
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
        class RegisteredType<T> : IRegisteredType
        {
            Action<Type, Func<ILifetime, object>> _registerFactory;
            Func<ILifetime, object> _factory;

            public RegisteredType(Action<Type, Func<ILifetime, object>> registerFactory, Func<ILifetime, object> factory)
            {
                _registerFactory = registerFactory;
                _factory = factory;

                registerFactory(typeof(T), _factory);
            }

            public void AsSingleton()
                => _registerFactory(typeof(T), lifetime => lifetime.GetServiceAsSingleton(typeof(T), _factory));

            public void PerScope() 
                => _registerFactory(typeof(T), lifetime => lifetime.GetServicePerScope(typeof(T), _factory));
        }
        #endregion
    }

    /// <summary>
    /// Extension methods for Container.IScope
    /// </summary>
    public static class IScopeExtensions
    {
        /// <summary>
        /// Returns an implementation of the specified interface
        /// </summary>
        /// <typeparam name="T">Interface type</typeparam>
        /// <returns>Object implementing the interface</returns>
        public static T Resolve<T>(this Container.IScope scope) => (T)scope.GetService(typeof(T));
    }
}