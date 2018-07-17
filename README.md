# MinIoC

MinIoC is a single-file, minimal C# IoC container. While there are several great IoC solutions available which are much more powerful and flexible, MinIoC aims to enable lightweight/small-footprint projects with a simple implementation. It is distributed as [a single .cs file](https://raw.githubusercontent.com/Microsoft/MinIoC/master/Container.cs) which can be included and compiled in other projects.

# Example

```c#
var container = new Container();

container.Register<IFoo>(typeof(Foo));
container.Register<IBar>(() => new Bar());
container.Register<IBaz>(typeof(Baz)).AsSingleton();

/* ... */

var baz = container.Resolve<IBaz>();
```

The example above calls the generic `Register<T>(Type)` and `Register<T>(Func<T>)` methods. The first one binds the generic parameter type to an actual type, the second binds the generic parameter type to a factory function. Calling `Resolve<T>()` creates an instance of the registered type.

For a type binding, the container uses the actual type's first constructor. All arguments need to also be resolvable by the container.

# API Details

The container implements `IServiceProvider`, exposing an `object GetService(Type type)` method and two non-generic registration methods: `Register(Type @interface, Func<object> factory)` and `Register(Type @interface, Type implementation)`.

Generic extension methods are provided both for registration (`Register<T>(Type)`, `Register<T>(Func<T>)`, `Register<T>()`) and resolution (`T Resolve<T>()`).

## Lifetimes

By default, each call to `Resolve<T>()` creates a new instance. Two other object lifetimes are supported: singleton and per-scope.

A singleton is created by calling `AsSingleton()` after registration:

```csharp
Container.Register<IFoo>(typeof(Foo)).AsSingleton();

var instance1 = Container.Resolve<IFoo>();
var instance2 = Container.Resolve<IFoo>();

Assert.AreEqual(instance1, instance2);
```

Scopes allow finer-grained lifetime control, where all types registered as per-scope are unique within a given scope. This allows singleton-like behavior within a scope but multiple object instances can be created across scopes. Scopes are created by calling `Container.CreateScope()` and they also expose a `Resolve<T>()` method: 

```csharp
Container.Register<IFoo>(typeof(Foo)).PerScope();
  
var instance1 = Container.Resolve<IFoo>();
var instance2 = Container.Resolve<IFoo>();

// Container is itself a scope
Assert.AreEqual(instance1, instance2);

using (var scope = Container.CreateScope())
{
    var instance3 = scope.Resolve<IFoo>();
    var instance4 = scope.Resolve<IFoo>();

    // Instances should be equal inside a scope
    Assert.AreEqual(instance3, instance4);
    
    // Instances should not be equal across scopes
    Assert.AreNotEqual(instance1, instance3);
}
```

# Contributing

This project welcomes contributions and suggestions.  Most contributions require you to agree to a
Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant us
the rights to use your contribution. For details, visit https://cla.microsoft.com.

When you submit a pull request, a CLA-bot will automatically determine whether you need to provide
a CLA and decorate the PR appropriately (e.g., label, comment). Simply follow the instructions
provided by the bot. You will only need to do this once across all repos using our CLA.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).
For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or
contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.
