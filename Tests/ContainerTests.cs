// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using System;
using System.Collections;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.MinIoC.Tests
{
#pragma warning disable 1591
    [TestClass]
    public class ContainerTests
    {
        private Container Container { get; set; }

        [TestInitialize]
        public void Initialize()
        {
            Container = new Container();
        }

        [TestMethod]
        public void SimpleReflectionConstruction()
        {
            Container.Register<IFoo>(typeof(Foo));

            object instance = Container.Resolve<IFoo>();

            // Instance should be of the registered type 
            Assert.IsInstanceOfType(instance, typeof(Foo));
        }

        [TestMethod]
        public void RecursiveReflectionConstruction()
        {
            Container.Register<IFoo>(typeof(Foo));
            Container.Register<IBar>(typeof(Bar));
            Container.Register<IBaz>(typeof(Baz));

            IBaz instance = Container.Resolve<IBaz>();

            // Test that the correct types were created
            Assert.IsInstanceOfType(instance, typeof(Baz));

            var baz = instance as Baz;
            Assert.IsInstanceOfType(baz.Bar, typeof(Bar));
            Assert.IsInstanceOfType(baz.Foo, typeof(Foo));
        }

        [TestMethod]
        public void SimpleFactoryConstruction()
        {
            Container.Register<IFoo>(() => new Foo());

            object instance = Container.Resolve<IFoo>();

            // Instance should be of the registered type 
            Assert.IsInstanceOfType(instance, typeof(Foo));
        }

        [TestMethod]
        public void MixedConstruction()
        {
            Container.Register<IFoo>(() => new Foo());
            Container.Register<IBar>(typeof(Bar));
            Container.Register<IBaz>(typeof(Baz));

            IBaz instance = Container.Resolve<IBaz>();

            // Test that the correct types were created
            Assert.IsInstanceOfType(instance, typeof(Baz));

            var baz = instance as Baz;
            Assert.IsInstanceOfType(baz.Bar, typeof(Bar));
            Assert.IsInstanceOfType(baz.Foo, typeof(Foo));
        }

        [TestMethod]
        public void InstanceResolution()
        {
            Container.Register<IFoo>(typeof(Foo));

            object instance1 = Container.Resolve<IFoo>();
            object instance2 = Container.Resolve<IFoo>();

            // Instances should be different between calls to Resolve
            Assert.AreNotEqual(instance1, instance2);
        }

        [TestMethod]
        public void SingletonResolution()
        {
            Container.Register<IFoo>(typeof(Foo)).AsSingleton();

            object instance1 = Container.Resolve<IFoo>();
            object instance2 = Container.Resolve<IFoo>();

            // Instances should be identic between calls to Resolve
            Assert.AreEqual(instance1, instance2);
        }

        [TestMethod]
        public void PerScopeResolution()
        {
            Container.Register<IFoo>(typeof(Foo)).PerScope();

            object instance1 = Container.Resolve<IFoo>();
            object instance2 = Container.Resolve<IFoo>();

            // Instances should be same as the container is itself a scope
            Assert.AreEqual(instance1, instance2);

            using (var scope = Container.CreateScope())
            {
                object instance3 = scope.Resolve<IFoo>();
                object instance4 = scope.Resolve<IFoo>();

                // Instances should be equal inside a scope
                Assert.AreEqual(instance3, instance4);

                // Instances should not be equal between scopes
                Assert.AreNotEqual(instance1, instance3);
            }
        }

        [TestMethod]
        public void MixedScopeResolution()
        {
            Container.Register<IFoo>(typeof(Foo)).PerScope();
            Container.Register<IBar>(typeof(Bar)).AsSingleton();
            Container.Register<IBaz>(typeof(Baz));

            using (var scope = Container.CreateScope())
            {
                Baz instance1 = scope.Resolve<IBaz>() as Baz;
                Baz instance2 = scope.Resolve<IBaz>() as Baz;

                // Ensure resolutions worked as expected
                Assert.AreNotEqual(instance1, instance2);

                // Singleton should be same
                Assert.AreEqual(instance1.Bar, instance2.Bar);
                Assert.AreEqual((instance1.Bar as Bar).Foo, (instance2.Bar as Bar).Foo);

                // Scoped types should be the same
                Assert.AreEqual(instance1.Foo, instance2.Foo);

                // Singleton should not hold scoped object
                Assert.AreNotEqual(instance1.Foo, (instance1.Bar as Bar).Foo);
                Assert.AreNotEqual(instance2.Foo, (instance2.Bar as Bar).Foo);
            }
        }

        [TestMethod]
        public void SingletonScopedResolution()
        {
            Container.Register<IFoo>(typeof(Foo)).AsSingleton();
            Container.Register<IBar>(typeof(Bar)).PerScope();

            var instance1 = Container.Resolve<IBar>();

            using (var scope = Container.CreateScope())
            {
                var instance2 = Container.Resolve<IBar>();

                // Singleton should resolve to the same instance
                Assert.AreEqual((instance1 as Bar).Foo, (instance2 as Bar).Foo);
            }
        }

        [TestMethod]
        public void MixedNoScopeResolution()
        {
            Container.Register<IFoo>(typeof(Foo)).PerScope();
            Container.Register<IBar>(typeof(Bar)).AsSingleton();
            Container.Register<IBaz>(typeof(Baz));

            Baz instance1 = Container.Resolve<IBaz>() as Baz;
            Baz instance2 = Container.Resolve<IBaz>() as Baz;

            // Ensure resolutions worked as expected
            Assert.AreNotEqual(instance1, instance2);

            // Singleton should be same
            Assert.AreEqual(instance1.Bar, instance2.Bar);

            // Scoped types should not be different outside a scope
            Assert.AreEqual(instance1.Foo, instance2.Foo);
            Assert.AreEqual(instance1.Foo, (instance1.Bar as Bar).Foo);
            Assert.AreEqual(instance2.Foo, (instance2.Bar as Bar).Foo);
        }

        [TestMethod]
        public void MixedReversedRegistration()
        {
            Container.Register<IBaz>(typeof(Baz));
            Container.Register<IBar>(typeof(Bar));
            Container.Register<IFoo>(() => new Foo());

            IBaz instance = Container.Resolve<IBaz>();

            // Test that the correct types were created
            Assert.IsInstanceOfType(instance, typeof(Baz));

            var baz = instance as Baz;
            Assert.IsInstanceOfType(baz.Bar, typeof(Bar));
            Assert.IsInstanceOfType(baz.Foo, typeof(Foo));
        }

        [TestMethod]
        public void ScopeDisposesOfCachedInstances()
        {
            Container.Register<SpyDisposable>(typeof(SpyDisposable)).PerScope();
            SpyDisposable spy;

            using (var scope = Container.CreateScope())
            {
                spy = scope.Resolve<SpyDisposable>();
            }

            Assert.IsTrue(spy.Disposed);
        }

        [TestMethod]
        public void ContainerDisposesOfSingletons()
        {
            SpyDisposable spy;
            using (var container = new Container())
            {
                container.Register<SpyDisposable>().AsSingleton();
                spy = container.Resolve<SpyDisposable>();
            }

            Assert.IsTrue(spy.Disposed);
        }

        [TestMethod]
        public void SingletonsAreDifferentAcrossContainers()
        {
            var container1 = new Container();
            container1.Register<IFoo>(typeof(Foo)).AsSingleton();

            var container2 = new Container();
            container2.Register<IFoo>(typeof(Foo)).AsSingleton();

            Assert.AreNotEqual(container1.Resolve<IFoo>(), container2.Resolve<IFoo>());
        }

        #region Types used for tests
        interface IFoo
        {
        }

        class Foo : IFoo
        {
        }

        interface IBar
        {
        }

        class Bar : IBar
        {
            public IFoo Foo { get; set; }

            public Bar(IFoo foo)
            {
                Foo = foo;
            }
        }

        interface IBaz
        {
        }

        class Baz : IBaz
        {
            public IFoo Foo { get; set; }
            public IBar Bar { get; set; }

            public Baz(IFoo foo, IBar bar)
            {
                Foo = foo;
                Bar = bar;
            }
        }

        class SpyDisposable : IDisposable
        {
            public bool Disposed { get; private set; }

            public void Dispose() => Disposed = true;
        }
        #endregion
    }
#pragma warning restore 1591
}
