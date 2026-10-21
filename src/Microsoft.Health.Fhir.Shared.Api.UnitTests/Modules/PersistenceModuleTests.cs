// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Modules;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Modules
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Validate)]
    public class PersistenceModuleTests
    {
        [Fact]
        public void GivenSearchFactory_WhenScopesAreDisposed_ThenEachInvocationOwnsAndDisposesItsServices()
        {
            var services = new ServiceCollection();
            int disposalCount = 0;
            services.AddScoped();
            services.AddScoped(_ => new DisposalProbe(() => disposalCount++));
            services.AddScoped<ISearchService>(provider =>
            {
                provider.GetRequiredService<DisposalProbe>();
                return Substitute.For<ISearchService>();
            });
            new PersistenceModule().Load(services);
            using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
            Func<IScoped<ISearchService>> factory = provider.GetRequiredService<Func<IScoped<ISearchService>>>();

            using (IScoped<ISearchService> first = factory())
            {
                using (IScoped<ISearchService> second = factory())
                {
                    Assert.NotSame(first.Value, second.Value);
                    Assert.Equal(0, disposalCount);
                }

                Assert.Equal(1, disposalCount);
            }

            Assert.Equal(2, disposalCount);
        }

        private sealed class DisposalProbe : IDisposable
        {
            private readonly Action _onDispose;

            public DisposalProbe(Action onDispose)
            {
                _onDispose = onDispose;
            }

            public void Dispose()
            {
                _onDispose();
            }
        }
    }
}
