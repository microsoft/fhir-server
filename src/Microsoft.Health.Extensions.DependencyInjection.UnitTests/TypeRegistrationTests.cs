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
        public void GivenADelegate_WhenRegisteringTransientAsImplementedInterfaces_ThenTheServicesIsRegistered()
        {
            new TypeRegistration(_collection, typeof(StreamReader), provider => new StreamReader(new MemoryStream()))
                .Transient()
                .AsImplementedInterfaces();

            Assert.Equal(typeof(IDisposable), _collection.Single().ServiceType);
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
                .AsService<IList<string>>();

            _collection.AddFactory<List<string>>();
            _collection.AddFactory<IList<string>>();

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

            _collection.AddTransient(typeof(IOwned<>), typeof(Owned<>));

            var ioc = _collection.BuildServiceProvider();

            var a = ioc.GetService<IOwned<List<string>>>();
            var b = ioc.GetService<IOwned<IList<string>>>();

            Assert.NotEqual(a.Value.GetHashCode(), b.Value.GetHashCode());
        }
    }
}
