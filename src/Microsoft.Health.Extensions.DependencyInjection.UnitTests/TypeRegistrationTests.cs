// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.Health.Extensions.DependencyInjection.UnitTests
{
    public class TypeRegistrationTests
    {
        private readonly ServiceCollection _collection;

        public TypeRegistrationTests()
        {
            _collection = new ServiceCollection();
        }

        [Fact]
        public void GivenAType_WhenRegisteringTransientAsSelf_ThenTheServicesIsRegistered()
        {
            new TypeRegistration(_collection, typeof(string))
                .Transient()
                .AsSelf();

            Assert.Equal(typeof(string), _collection.Single().ServiceType);
        }

        [Fact]
        public void GivenAType_WhenRegisteringTransientAsSelf_ThenTheServiceGivesNewInstances()
        {
            new TypeRegistration(_collection, typeof(List<string>))
                .Transient()
                .AsSelf();

            var ioc = _collection.BuildServiceProvider();

            Assert.NotEqual(ioc.GetService<List<string>>().GetHashCode(), ioc.GetService<List<string>>().GetHashCode());
        }

        [Fact]
        public void GivenAType_WhenRegisteringTransientAsSelfAsService_ThenTheServicesIsRegistered()
        {
            new TypeRegistration(_collection, typeof(StreamReader))
                .Transient()
                .AsSelf()
                .AsService<TextReader>();

            Assert.Equal(typeof(StreamReader), _collection.First().ServiceType);
            Assert.Equal(typeof(TextReader), _collection.Skip(1).First().ServiceType);
        }

        [Fact]
        public void GivenAType_WhenRegisteringAndReplacingService_ThenTheNewServicesIsRegistered()
        {
            new TypeRegistration(_collection, typeof(StreamReader))
                .Transient()
                .AsService<TextReader>();

            new TypeRegistration(_collection, typeof(StringReader))
                .Transient()
                .ReplaceService<TextReader>();

            Assert.Collection(_collection, x =>
            {
                Assert.Equal(typeof(StringReader), x.ImplementationType);
                Assert.Equal(typeof(TextReader), x.ServiceType);
            });
        }

        [Fact]
        public void GivenAFactory_WhenReplacingSelf_ThenOnlyTheNewServiceIsRegistered()
        {
            _collection
                .Add(sp => "a")
                .Transient()
                .AsSelf();

            _collection
                .Add(sp => "b")
                .Transient()
                .ReplaceSelf();

            Assert.Single(_collection.BuildServiceProvider().GetService<IEnumerable<string>>(), "b");
        }

        [Fact]
        public void GivenADelegate_WhenRegisteringTransientAsSelf_ThenTheServicesIsRegistered()
        {
            new TypeRegistration(_collection, typeof(StreamReader), provider => new StreamReader(new MemoryStream()))
                .Transient()
                .AsSelf()
                .AsService<TextReader>();

            Assert.Equal(typeof(StreamReader), _collection.First().ServiceType);
            Assert.Equal(typeof(TextReader), _collection.Skip(1).First().ServiceType);
        }

        [Fact]
        public void GivenADelegate_WhenRegisteringTransientAsImplementedInterfaces_ThenIDisposableIsNotRegistered()
        {
            new TypeRegistration(_collection, typeof(TestDisposableObjectWithInterface))
                .Transient()
                .AsImplementedInterfaces();

            Assert.True(_collection.All(x => x.ServiceType != typeof(IDisposable)));
        }

        [Fact]
        public void GivenADelegate_WhenRegisteringTransientAsImplementedInterfaces_ThenTheServicesIsRegistered()
        {
            new TypeRegistration(_collection, typeof(TestDisposableObjectWithInterface))
                .Transient()
                .AsImplementedInterfaces();

            Assert.Equal(typeof(IEquatable<string>), _collection.Single().ServiceType);
        }

        [Fact]
        public void GivenADelegate_WhenRegisteringTransientAsImplementedInterfaces_ThenTheServicesWithDelegateIsRegistered()
        {
            new TypeRegistration(_collection, typeof(TestDisposableObjectWithInterface))
                .Transient()
                .AsImplementedInterfaces(x => typeof(IEquatable<string>).IsAssignableFrom(x));

            Assert.Equal(typeof(IEquatable<string>), _collection.Single().ServiceType);
        }

        [Fact]
        public void GivenADelegate_WhenRegisteringScopedAsSelfAsServices_ThenTheSameInstanceIsResolvedForBoth()
        {
            new TypeRegistration(_collection, typeof(List<string>), provider => new List<string> { Guid.NewGuid().ToString() })
                .Scoped()
                .AsSelf()
                .AsService<IList<string>>()
                .AsImplementedInterfaces();

            var ioc = _collection.BuildServiceProvider();

            var a = ioc.GetService<List<string>>();
            var b = ioc.GetService<IList<string>>();
            var c = ioc.GetService<IEnumerable<string>>();

            Assert.Equal(a.First(), b.First());
            Assert.Equal(a.First(), c.First());
        }

        [Fact]
        public void GivenAType_WhenRegisteringScopedAsSelfAsServices_ThenTheSameInstanceIsResolvedForBoth()
        {
            new TypeRegistration(_collection, typeof(List<string>))
                .Scoped()
                .AsSelf()
                .AsService<IList<string>>();

            var ioc = _collection.BuildServiceProvider();

            var a = ioc.GetService<List<string>>();
            var b = ioc.GetService<IList<string>>();

            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void GivenADelegate_WhenRegisteringSingletonAsSelfAsServices_ThenTheSameInstanceIsResolvedForBoth()
        {
            new TypeRegistration(_collection, typeof(List<string>), provider => new List<string> { Guid.NewGuid().ToString() })
                .Singleton()
                .AsSelf()
                .AsService<IList<string>>()
                .AsImplementedInterfaces();

            var ioc = _collection.BuildServiceProvider();

            var a = ioc.GetService<List<string>>();
            var b = ioc.GetService<IList<string>>();
            var c = ioc.GetService<IEnumerable<string>>();

            Assert.Equal(a.First(), b.First());
            Assert.Equal(a.First(), c.First());
        }

        [Fact]
        public void GivenAType_WhenRegisteringSingletonAsSelfAsServices_ThenTheSameInstanceIsResolvedForBoth()
        {
            new TypeRegistration(_collection, typeof(List<string>))
                .Singleton()
                .AsSelf()
                .AsService<IList<string>>();

            var ioc = _collection.BuildServiceProvider();

            var a = ioc.GetService<List<string>>();
            var b = ioc.GetService<IList<string>>();

            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void GivenAType_WhenRegisteringLazy_ThenTypeCanBeResolved()
        {
            new TypeRegistration(_collection, typeof(List<string>))
                .Singleton()
                .AsSelf()
                .AsService<IList<string>>();

            _collection.AddTransient(typeof(Lazy<>), typeof(LazyProvider<>));

            var ioc = _collection.BuildServiceProvider();

            var a = ioc.GetService<Lazy<List<string>>>();
            var b = ioc.GetService<Lazy<IList<string>>>();

            Assert.Equal(a.Value.GetHashCode(), b.Value.GetHashCode());
        }

        [Fact]
        public void GivenAType_WhenRegisteringAFactory_ThenTypeCanBeResolved()
        {
            new TypeRegistration(_collection, typeof(List<string>))
                .Singleton()
                .AsSelf()
                .AsService<IList<string>>()
                .AsFactory<List<string>>()
                .AsFactory<IList<string>>();

            var ioc = _collection.BuildServiceProvider();

            var a = ioc.GetService<Func<List<string>>>();
            var b = ioc.GetService<Func<IList<string>>>();

            Assert.Equal(a().GetHashCode(), b().GetHashCode());
        }

        [Fact]
        public void GivenAType_WhenRegisteringOwned_ThenTypeCanBeResolvedInDifferentScope()
        {
            new TypeRegistration(_collection, typeof(List<string>))
                .Scoped()
                .AsSelf()
                .AsService<IList<string>>();

            _collection.AddTransient(typeof(IScoped<>), typeof(Scoped<>));

            var ioc = _collection.BuildServiceProvider();

            var a = ioc.GetService<IScoped<List<string>>>();
            var b = ioc.GetService<IScoped<IList<string>>>();

            Assert.NotEqual(a.Value.GetHashCode(), b.Value.GetHashCode());
        }

        [Fact]
        public void GivenAType_WhenRegisteringScopedWithExplicitRegistration_ThenOnlyExplicitRegistrationIsReturned()
        {
            // Add open generic first
            _collection.AddScoped();

            // Adds actual List
            _collection.Add<List<string>>()
                .Transient()
                .AsService<IList<string>>();

            // Adds explicit Scope registration
            _collection.Add(x => new TestScope(x.GetService<IList<string>>()))
                .Scoped()
                .AsService<IScoped<IList<string>>>();

            var ioc = _collection.BuildServiceProvider();

            var resolvedService = ioc.GetService<IScoped<IList<string>>>();

            Assert.IsType<TestScope>(resolvedService);
        }

        [Fact]
        public void GivenAType_WhenRegisteringScopedWithExplicitRegistrationReverseOrder_ThenOnlyExplicitRegistrationIsReturned()
        {
            // Adds actual List
            _collection.Add<List<string>>()
                .Transient()
                .AsService<IList<string>>();

            // Adds explicit Scope registration
            _collection.Add(x => new TestScope(x.GetService<IList<string>>()))
                .Scoped()
                .AsService<IScoped<IList<string>>>();

            // Add open generic second
            _collection.AddScoped();

            var ioc = _collection.BuildServiceProvider();

            var resolvedService = ioc.GetService<IScoped<IList<string>>>();

            Assert.IsType<TestScope>(resolvedService);
        }

        [Fact]
        public void GivenAType_WhenRegisteringAsSelf_ThenTheTypeAppearsAsMetadataOnTheServiceDescriptor()
        {
            _collection.Add<List<string>>()
                .Transient()
                .AsSelf();

            Assert.Equal(typeof(List<string>), (_collection.Single() as ServiceDescriptorWithMetadata)?.Metadata);
        }

        [Fact]
        public void GivenAType_WhenRegisteringAsSelfAndAsAnotherService_ThenTheTypeAppearsAsMetadataOnTheServiceDescriptor()
        {
            _collection.Add<List<string>>()
                .Transient()
                .AsSelf()
                .AsService<IList<string>>();

            Assert.All(_collection, sd => Assert.Equal(typeof(List<string>), (sd as ServiceDescriptorWithMetadata)?.Metadata));
        }

        [Fact]
        public void GivenFactory_WhenRegisteringAsSelfAsAService_ThenTheTypeAppearsAsMetadataOnTheServiceDescriptor()
        {
            _collection.Add(sp => new List<string>())
                .Transient()
                .AsService<IList<string>>();

            Assert.All(_collection, sd => Assert.Equal(typeof(List<string>), (sd as ServiceDescriptorWithMetadata)?.Metadata));
        }

        private class TestScope : IScoped<IList<string>>
        {
            public TestScope(IList<string> value)
            {
                Value = value;
            }

            public IList<string> Value { get; }

            public void Dispose()
            {
            }
        }

        private class TestDisposableObjectWithInterface : IEquatable<string>, IDisposable
        {
            public bool Equals(string other)
            {
                throw new NotImplementedException();
            }

            public void Dispose()
            {
                throw new NotImplementedException();
            }
        }
    }
}
