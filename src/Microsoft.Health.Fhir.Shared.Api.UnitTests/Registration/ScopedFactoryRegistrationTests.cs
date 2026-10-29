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
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Registration
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Validate)]
    public class ScopedFactoryRegistrationTests
    {
        [Fact]
        public void GivenRootResolvedSearchFactory_WhenScopeIsDisposed_ThenWrapperIsNotRetained()
        {
            AssertCollectible(() => Substitute.For<ISearchService>(), nameof(ISearchService));
        }

        [Fact]
        public void GivenRootResolvedDataStoreFactory_WhenScopeIsDisposed_ThenWrapperIsNotRetained()
        {
            AssertCollectible(() => Substitute.For<IFhirDataStore>(), nameof(IFhirDataStore));
        }

        [Fact]
        public void GivenRootResolvedOperationDataStoreFactory_WhenScopeIsDisposed_ThenWrapperIsNotRetained()
        {
            AssertCollectible(() => Substitute.For<IFhirOperationDataStore>(), nameof(IFhirOperationDataStore));
        }

        [Fact]
        public void GivenRootResolvedDeletionFactory_WhenScopeIsDisposed_ThenWrapperIsNotRetained()
        {
            AssertCollectible(() => Substitute.For<IDeletionService>(), nameof(IDeletionService));
        }

        [Fact]
        public void GivenRootResolvedBulkUpdateFactory_WhenScopeIsDisposed_ThenWrapperIsNotRetained()
        {
            AssertCollectible(() => Substitute.For<IBulkUpdateService>(), nameof(IBulkUpdateService));
        }

        [Fact]
        public void GivenRootResolvedProvideCapabilityFactory_WhenScopeIsDisposed_ThenWrapperIsNotRetained()
        {
            AssertCollectible<IEnumerable<IProvideCapability>>(() => new[] { Substitute.For<IProvideCapability>() }, "IEnumerable<IProvideCapability>");
        }

        [Fact]
        public void GivenRootResolvedInstantiateCapabilityFactory_WhenScopeIsDisposed_ThenWrapperIsNotRetained()
        {
            AssertCollectible<IEnumerable<IInstantiateCapability>>(() => new[] { Substitute.For<IInstantiateCapability>() }, "IEnumerable<IInstantiateCapability>");
        }

        [Fact]
        public void GivenRootResolvedAnonymizerFactory_WhenScopeIsDisposed_ThenWrapperIsNotRetained()
        {
            Assert.SkipWhen(ModelInfoProvider.Version is not (FhirSpecification.Stu3 or FhirSpecification.R4), "Anonymization is available only in STU3 and R4.");
            AssertCollectible(() => Substitute.For<IAnonymizer>(), nameof(IAnonymizer));
        }

        [Fact]
        public void GivenRootResolvedAnonymizerFactoryFactory_WhenScopeIsDisposed_ThenWrapperIsNotRetained()
        {
            AssertCollectible(() => Substitute.For<IAnonymizerFactory>(), nameof(IAnonymizerFactory));
        }

        [Fact]
        public void GivenRootResolvedJobFactory_WhenScopeIsDisposed_ThenWrapperIsNotRetained()
        {
            AssertCollectible<IEnumerable<Func<IJob>>>(() => new Func<IJob>[] { () => Substitute.For<IJob>() }, "IEnumerable<Func<IJob>>");
        }

        private static void AssertCollectible<T>(Func<T> createPayload, string caseName)
            where T : class
        {
            var services = new ServiceCollection();
            IConfiguration configuration = new ConfigurationBuilder().Build();
            services.AddFhirServer(configuration);
            services.AddScoped(_ => createPayload());
            using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

            ScopedFactoryAssert.NotRetainedByRoot<T>(provider, caseName);
        }
    }
}
