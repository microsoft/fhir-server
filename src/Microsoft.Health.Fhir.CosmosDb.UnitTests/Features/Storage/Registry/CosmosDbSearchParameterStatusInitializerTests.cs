// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Health.Core;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.CosmosDb.Configs;
using Microsoft.Health.Fhir.CosmosDb.Features.Queries;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Registry;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Storage.Registry
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class CosmosDbSearchParameterStatusInitializerTests
    {
        private readonly CosmosDbSearchParameterStatusInitializer _initializer;
        private readonly ICosmosQueryFactory _cosmosDocumentQueryFactory;
        private readonly Uri _testParameterUri;

        public CosmosDbSearchParameterStatusInitializerTests()
        {
            ISearchParameterStatusDataStore searchParameterStatusDataStore = Substitute.For<ISearchParameterStatusDataStore>();
            _cosmosDocumentQueryFactory = Substitute.For<ICosmosQueryFactory>();

            _initializer = new CosmosDbSearchParameterStatusInitializer(
                () => searchParameterStatusDataStore,
                _cosmosDocumentQueryFactory,
                new CosmosDataStoreConfiguration());

            _testParameterUri = new Uri("/test", UriKind.Relative);
            searchParameterStatusDataStore
                .GetSearchParameterStatuses(Arg.Any<CancellationToken>())
                .Returns(new[]
                {
                    new ResourceSearchParameterStatus
                    {
                      Uri = _testParameterUri,
                      Status = SearchParameterStatus.Enabled,
                      LastUpdated = Clock.UtcNow,
                      IsPartiallySupported = false,
                    },
                });
        }

        [Fact]
        public async Task GivenARegistryInitializer_WhenDatabaseIsNew_SearchParametersShouldBeUpserted()
        {
            ICosmosQuery<dynamic> cosmosQuery = Substitute.For<ICosmosQuery<dynamic>>();
            _cosmosDocumentQueryFactory.Create<dynamic>(Arg.Any<Container>(), Arg.Any<CosmosQueryContext>())
                .Returns(cosmosQuery);

            cosmosQuery
                .ExecuteNextAsync()
                .Returns(Substitute.ForPartsOf<FeedResponse<dynamic>>());

            Container container = Substitute.For<Container>();

            await _initializer.ExecuteAsync(container, CancellationToken.None);

            container.Received().CreateTransactionalBatch(Arg.Any<PartitionKey>());
        }

        [Fact]
        public async Task GivenARegistryInitializer_WhenDatabaseIsExisting_NothingNeedsToBeDone()
        {
            ICosmosQuery<dynamic> documentQuery = Substitute.For<ICosmosQuery<dynamic>>();

            _cosmosDocumentQueryFactory.Create<dynamic>(Arg.Any<Container>(), Arg.Any<CosmosQueryContext>())
                .Returns(documentQuery);

            var response = Substitute.ForPartsOf<FeedResponse<dynamic>>();

            response.GetEnumerator()
                .Returns(new List<dynamic> { new SearchParameterStatusWrapper() }.GetEnumerator());

            documentQuery
                .ExecuteNextAsync()
                .Returns(info => response);

            Container container = Substitute.For<Container>();

            await _initializer.ExecuteAsync(container, CancellationToken.None);

            container.DidNotReceive().CreateTransactionalBatch(Arg.Any<PartitionKey>());
        }
    }
}
