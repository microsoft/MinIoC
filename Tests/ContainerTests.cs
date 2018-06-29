// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using System.Collections;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.MinIoC.Tests
{
    [TestClass]
    public class ContainerTests
    {
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

            // Instances should be different if not in a scope
            Assert.AreNotEqual(instance1, instance2);

            using (var scope = Container.CreateScope())
            {
                object instance3 = scope.Resolve<IFoo>();
                object instance4 = scope.Resolve<IFoo>();

                // Instances should be equal inside a scope
                Assert.AreEqual(instance3, instance4);
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

            // Scoped types should be different outside a scope
            Assert.AreNotEqual(instance1.Foo, instance2.Foo);
            Assert.AreNotEqual(instance1.Foo, (instance1.Bar as Bar).Foo);
            Assert.AreNotEqual(instance2.Foo, (instance2.Bar as Bar).Foo);
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

        [TestCleanup]
        public void TearDown()
        {
            // Clear registered types after each test
            (new PrivateType(typeof(Container)).GetStaticField("_registeredTypes") as IDictionary).Clear();
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
        #endregion
    }
}
