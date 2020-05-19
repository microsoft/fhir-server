// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Linq;
using Microsoft.Health.Core;
using Microsoft.Health.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Registry;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Storage.Registry
{
    public class CosmosDbStatusRegistryInitializerTests
    {
        private readonly CosmosDbStatusRegistryInitializer _initializer;
        private readonly ICosmosDocumentQueryFactory _cosmosDocumentQueryFactory;
        private readonly Uri _testParameterUri;

        public CosmosDbStatusRegistryInitializerTests()
        {
            ISearchParameterRegistryDataStore searchParameterRegistry = Substitute.For<ISearchParameterRegistryDataStore>();
            _cosmosDocumentQueryFactory = Substitute.For<ICosmosDocumentQueryFactory>();

            _initializer = new CosmosDbStatusRegistryInitializer(
                () => searchParameterRegistry,
                _cosmosDocumentQueryFactory);

            _testParameterUri = new Uri("/test", UriKind.Relative);
            searchParameterRegistry
                .GetSearchParameterStatuses()
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
            IDocumentQuery<dynamic> documentQuery = Substitute.For<IDocumentQuery<dynamic>>();
            _cosmosDocumentQueryFactory.Create<dynamic>(Arg.Any<IDocumentClient>(), Arg.Any<CosmosQueryContext>())
                .Returns(documentQuery);

            documentQuery
                .ExecuteNextAsync()
                .Returns(new FeedResponse<dynamic>(new dynamic[0]));

            IDocumentClient documentClient = Substitute.For<IDocumentClient>();
            var relativeCollectionUri = new Uri("/collection1", UriKind.Relative);

            await _initializer.ExecuteAsync(documentClient, new DocumentCollection(), relativeCollectionUri);

            await documentClient.Received().UpsertDocumentAsync(
                relativeCollectionUri,
                Arg.Is<SearchParameterStatusWrapper>(x => x.Uri == _testParameterUri));
        }

        [Fact]
        public async Task GivenARegistryInitializer_WhenDatabaseIsExisting_NothingNeedsToBeDone()
        {
            IDocumentQuery<dynamic> documentQuery = Substitute.For<IDocumentQuery<dynamic>>();
            _cosmosDocumentQueryFactory.Create<dynamic>(Arg.Any<IDocumentClient>(), Arg.Any<CosmosQueryContext>())
                .Returns(documentQuery);

            documentQuery
                .ExecuteNextAsync()
                .Returns(new FeedResponse<dynamic>(new dynamic[] { new SearchParameterStatusWrapper() }));

            IDocumentClient documentClient = Substitute.For<IDocumentClient>();
            var relativeCollectionUri = new Uri("/collection1", UriKind.Relative);

            await _initializer.ExecuteAsync(documentClient, new DocumentCollection(), relativeCollectionUri);

            await documentClient.DidNotReceive().UpsertDocumentAsync(
                relativeCollectionUri,
                Arg.Any<SearchParameterStatusWrapper>());
        }
    }
}
