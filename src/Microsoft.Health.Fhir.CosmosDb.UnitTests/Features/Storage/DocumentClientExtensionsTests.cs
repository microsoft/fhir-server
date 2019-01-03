// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Health.CosmosDb.Features.Storage;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Storage
{
    public class DocumentClientExtensionsTests
    {
        private IDocumentClient _documentClient = Substitute.For<IDocumentClient>();

        [Fact]
        public async Task GivenDocumentCollectionAlreadyExists_WhenCreatingDocumentCollectionIfNotExists_ThenIndexingPolicyShouldBeUpdated()
        {
            Uri collectionUri = new Uri("http://database/collection");

            IndexingPolicy expectedIndexingPolicy = new IndexingPolicy();

            DocumentCollection documentCollection = new DocumentCollection()
            {
                IndexingPolicy = expectedIndexingPolicy,
            };

            DocumentCollection existingDocumentCollection = new DocumentCollection();

            _documentClient.ReadDocumentCollectionAsync(collectionUri, Arg.Any<RequestOptions>()).Returns(
                new ResourceResponse<DocumentCollection>(existingDocumentCollection));

            // Check to make sure the index policy is requested to be updated.
            _documentClient.ReplaceDocumentCollectionAsync(
                Arg.Is<DocumentCollection>(x => x == existingDocumentCollection && x.IndexingPolicy == expectedIndexingPolicy),
                Arg.Any<RequestOptions>())
                .Returns(new ResourceResponse<DocumentCollection>(existingDocumentCollection));

            DocumentCollection result = await _documentClient.CreateDocumentCollectionIfNotExistsAsync(new Uri("http://database"), collectionUri, documentCollection);

            Assert.NotNull(result);
        }
    }
}
