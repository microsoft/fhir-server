// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Registration
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Validate)]
    public class ScopedFactoryRegistrationTests
    {
        public enum ScopedService
        {
            Search,
            DataStore,
            OperationDataStore,
            Deletion,
            BulkUpdate,
            ProvideCapability,
            InstantiateCapability,
            Anonymizer,
        }

        public static TheoryData<ScopedService> ScopedServices
        {
            get
            {
                var services = new TheoryData<ScopedService>
                {
                    ScopedService.Search,
                    ScopedService.DataStore,
                    ScopedService.OperationDataStore,
                    ScopedService.Deletion,
                    ScopedService.BulkUpdate,
                    ScopedService.ProvideCapability,
                    ScopedService.InstantiateCapability,
                };

                if (ModelInfoProvider.Version is FhirSpecification.Stu3 or FhirSpecification.R4)
                {
                    services.Add(ScopedService.Anonymizer);
                }

                return services;
            }
        }

        [Theory]
        [MemberData(nameof(ScopedServices), DisableDiscoveryEnumeration = true)]
        public void GivenRootResolvedFactory_WhenScopeIsDisposed_ThenWrapperIsNotRetained(ScopedService service)
        {
            var services = new ServiceCollection();
            IConfiguration configuration = new ConfigurationBuilder().Build();
            services.AddFhirServer(configuration);

            switch (service)
            {
                case ScopedService.Search:
                    AssertCollectible(services, () => Substitute.For<ISearchService>(), nameof(ISearchService));
                    break;
                case ScopedService.DataStore:
                    AssertCollectible(services, () => Substitute.For<IFhirDataStore>(), nameof(IFhirDataStore));
                    break;
                case ScopedService.OperationDataStore:
                    AssertCollectible(services, () => Substitute.For<IFhirOperationDataStore>(), nameof(IFhirOperationDataStore));
                    break;
                case ScopedService.Deletion:
                    AssertCollectible(services, () => Substitute.For<IDeletionService>(), nameof(IDeletionService));
                    break;
                case ScopedService.BulkUpdate:
                    AssertCollectible(services, () => Substitute.For<IBulkUpdateService>(), nameof(IBulkUpdateService));
                    break;
                case ScopedService.ProvideCapability:
                    AssertCollectible<IEnumerable<IProvideCapability>>(services, () => new[] { Substitute.For<IProvideCapability>() }, "IEnumerable<IProvideCapability>");
                    break;
                case ScopedService.InstantiateCapability:
                    AssertCollectible<IEnumerable<IInstantiateCapability>>(services, () => new[] { Substitute.For<IInstantiateCapability>() }, "IEnumerable<IInstantiateCapability>");
                    break;
                case ScopedService.Anonymizer:
                    AssertCollectible(services, () => Substitute.For<IAnonymizer>(), nameof(IAnonymizer));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(service));
            }
        }

        private static void AssertCollectible<T>(IServiceCollection services, Func<T> createPayload, string caseName)
            where T : class
        {
            services.AddScoped(_ => createPayload());
            using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

            ScopedFactoryAssert.NotRetainedByRoot<T>(provider, caseName);
        }
    }
}
