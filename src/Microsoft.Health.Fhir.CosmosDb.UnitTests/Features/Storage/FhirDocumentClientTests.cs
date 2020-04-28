// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NSubstitute.ReturnsExtensions;
using Xunit;
using FhirDocumentClient = Microsoft.Health.Fhir.CosmosDb.Features.Storage.FhirDocumentClient;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Storage
{
    public class FhirDocumentClientTests
    {
        private readonly IDocumentClient _innerClient;
        private readonly Dictionary<string, StringValues> _requestHeaders = new Dictionary<string, StringValues>();
        private readonly Dictionary<string, StringValues> _responseHeaders = new Dictionary<string, StringValues>();
        private readonly IDocumentClient _fhirClient;
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;
        private readonly ICosmosResponseProcessor _cosmosResponseProcessor;

        public FhirDocumentClientTests()
        {
            _innerClient = Substitute.For<IDocumentClient>();
            _fhirRequestContextAccessor = Substitute.For<IFhirRequestContextAccessor>();
            _fhirRequestContextAccessor.FhirRequestContext.RequestHeaders.Returns(_requestHeaders);
            _fhirRequestContextAccessor.FhirRequestContext.ResponseHeaders.Returns(_responseHeaders);

            _cosmosResponseProcessor = Substitute.For<ICosmosResponseProcessor>();

            _fhirClient = new FhirDocumentClient(_innerClient, _fhirRequestContextAccessor, null, _cosmosResponseProcessor);
        }

        [Fact]
        public async Task GivenACreateRequest_WithSessionConsistency_ThenTheResponseHeadersContainTheSessionToken()
        {
            _innerClient
                .CreateDocumentAsync("coll", (1, 2), Arg.Is<RequestOptions>(o => o.ConsistencyLevel == ConsistencyLevel.Session && o.SessionToken == "1"))
                .Returns(CosmosDbMockingHelper.CreateResourceResponse(new Document(), HttpStatusCode.OK, new NameValueCollection { { CosmosDbHeaders.SessionToken, "2" } }));

            _requestHeaders.Add(CosmosDbHeaders.ConsistencyLevel, "Session");
            _requestHeaders.Add(CosmosDbHeaders.SessionToken, "1");

            await _fhirClient.CreateDocumentAsync("coll", (1, 2));

            await _cosmosResponseProcessor.Received(1).ProcessResponse(Arg.Any<ResourceResponseBase>());
        }

        [Fact]
        public async Task GivenACreateRequest_WithWithNoConsistencySpecifiedAndNoRequestOptions_ThenNoRequestOptionsAreCreated()
        {
            _innerClient.CreateDocumentAsync("coll", (1, 2)).Returns(CosmosDbMockingHelper.CreateResourceResponse(new Document(), HttpStatusCode.OK, new NameValueCollection()));
            await _fhirClient.CreateDocumentAsync("coll", (1, 2));
            await _innerClient.Received().CreateDocumentAsync("coll", (1, 2));

            await _cosmosResponseProcessor.Received(1).ProcessResponse(Arg.Any<ResourceResponseBase>());
        }

        [Fact]
        public async Task GivenACreateRequest_WithNoFhirContext_ThenNoRequestOptionsAreCreated()
        {
            _fhirRequestContextAccessor.FhirRequestContext.ReturnsNull();
            _innerClient.CreateDocumentAsync("coll", (1, 2)).Returns(CosmosDbMockingHelper.CreateResourceResponse(new Document(), HttpStatusCode.OK, new NameValueCollection()));
            await _fhirClient.CreateDocumentAsync("coll", (1, 2));
            await _innerClient.Received().CreateDocumentAsync("coll", (1, 2));

            await _cosmosResponseProcessor.Received(1).ProcessResponse(Arg.Any<ResourceResponseBase>());
        }

        [Fact]
        public async Task GivenAFeedRequest_WithSessionConsistency_ThenTheResponseHeadersContainTheSessionToken()
        {
            _innerClient
                .ReadDatabaseFeedAsync(Arg.Is<FeedOptions>(o => o.ConsistencyLevel == ConsistencyLevel.Session && o.SessionToken == "1"))
                .Returns(new FeedResponse<Database>(Enumerable.Empty<Database>()));

            _requestHeaders.Add(CosmosDbHeaders.ConsistencyLevel, "Session");
            _requestHeaders.Add(CosmosDbHeaders.SessionToken, "1");

            await _fhirClient.ReadDatabaseFeedAsync();

            await _cosmosResponseProcessor.Received(1).ProcessResponse(Arg.Any<FeedResponse<Database>>());
        }

        [Fact]
        public async Task GivenAFeedRequest_WithWithNoConsistencySpecifiedAndNoRequestOptions_ThenNoRequestOptionsAreCreated()
        {
            _innerClient.ReadDatabaseFeedAsync()
                .ReturnsForAnyArgs(new FeedResponse<Database>(Enumerable.Empty<Database>()));
            await _fhirClient.ReadDatabaseFeedAsync();
            await _innerClient.Received().ReadDatabaseFeedAsync();

            await _cosmosResponseProcessor.Received(1).ProcessResponse(Arg.Any<FeedResponse<Database>>());
        }

        [Fact]
        public async Task GivenAFeedRequest_WithAnUnrecognizedConsistencyLevelHeaderValue_TheProcessExceptionIsCalled()
        {
            _requestHeaders.Add(CosmosDbHeaders.ConsistencyLevel, "CatsAndDogs");

            await Assert.ThrowsAsync<BadRequestException>(() => _fhirClient.ReadDatabaseFeedAsync());

            await _cosmosResponseProcessor.Received(1).ProcessException(Arg.Any<BadRequestException>());
        }

        [Fact]
        public async Task GivenAStoredProcedureRequestWithLink_WithSessionConsistency_ThenTheResponseHeadersContainTheSessionToken()
        {
            _innerClient
                .ExecuteStoredProcedureAsync<int>("link", Arg.Is<RequestOptions>(o => o.ConsistencyLevel == ConsistencyLevel.Session))
                .Returns(CosmosDbMockingHelper.CreateStoredProcedureResponse(42, HttpStatusCode.OK, new NameValueCollection { { CosmosDbHeaders.SessionToken, "2" } }));

            _requestHeaders.Add(CosmosDbHeaders.ConsistencyLevel, "Session");
            _requestHeaders.Add(CosmosDbHeaders.SessionToken, "1");

            await _fhirClient.ExecuteStoredProcedureAsync<int>("link");

            await _cosmosResponseProcessor.Received(1).ProcessResponse(Arg.Any<StoredProcedureResponse<int>>());
        }

        [Fact]
        public async Task GivenAStoredProcedureRequestWithUri_WithSessionConsistency_ThenTheResponseHeadersContainTheSessionToken()
        {
            _innerClient
                .ExecuteStoredProcedureAsync<int>(default(Uri), Arg.Is<RequestOptions>(o => o.ConsistencyLevel == ConsistencyLevel.Session))
                .Returns(CosmosDbMockingHelper.CreateStoredProcedureResponse(42, HttpStatusCode.OK, new NameValueCollection { { CosmosDbHeaders.SessionToken, "2" } }));

            _requestHeaders.Add(CosmosDbHeaders.ConsistencyLevel, "Session");
            _requestHeaders.Add(CosmosDbHeaders.SessionToken, "1");

            await _fhirClient.ExecuteStoredProcedureAsync<int>(default(Uri));

            await _cosmosResponseProcessor.Received(1).ProcessResponse(Arg.Any<StoredProcedureResponse<int>>());
        }

        [InlineData(ConsistencyLevel.Eventual, ConsistencyLevel.Strong)]
        [InlineData(ConsistencyLevel.Eventual, ConsistencyLevel.BoundedStaleness)]
        [InlineData(ConsistencyLevel.Session, ConsistencyLevel.Strong)]
        [InlineData(ConsistencyLevel.Session, ConsistencyLevel.BoundedStaleness)]
        [InlineData(ConsistencyLevel.BoundedStaleness, ConsistencyLevel.Strong)]
        [Theory]
        public async Task GivenAFeedRequest_WithAStrongerConsistencyLevelThanTheDefault_ThenABadRequestExceptionIsThrown(ConsistencyLevel defaultConsistencyLevel, ConsistencyLevel requestedConsistencyLevel)
        {
            _innerClient.ConsistencyLevel.Returns(defaultConsistencyLevel);
            _requestHeaders.Add(CosmosDbHeaders.ConsistencyLevel, requestedConsistencyLevel.ToString());

            await Assert.ThrowsAsync<BadRequestException>(() => _fhirClient.ReadDatabaseFeedAsync());

            await _cosmosResponseProcessor.Received(1).ProcessException(Arg.Any<BadRequestException>());
        }

        [Fact]
        public async Task GivenAFeedRequest_WhenMaxContinuationSizeIsSet_ThenFeedRequestIsUpdated()
        {
            IDocumentClient client = new FhirDocumentClient(_innerClient, _fhirRequestContextAccessor, 5, _cosmosResponseProcessor);

            _innerClient
                .ReadDatabaseFeedAsync(Arg.Is<FeedOptions>(o => o.ResponseContinuationTokenLimitInKb == 5))
                .Returns(new FeedResponse<Database>(Enumerable.Empty<Database>()));

            await client.ReadDatabaseFeedAsync();

            await _cosmosResponseProcessor.Received(1).ProcessResponse(Arg.Any<FeedResponse<Database>>());
        }

        [Fact]
        public async Task GivenTwoFeedRequests_WhenExecuted_ThenTheResponseHeadersContainTheCumulativeRequestCharge()
        {
            _innerClient
                .ReadDatabaseFeedAsync(Arg.Any<FeedOptions>())
                .Returns(new FeedResponse<Database>(Enumerable.Empty<Database>()));

            await _fhirClient.ReadDatabaseFeedAsync();
            await _fhirClient.ReadDatabaseFeedAsync();

            await _cosmosResponseProcessor.Received(2).ProcessResponse(Arg.Any<FeedResponse<Database>>());
        }

        [Fact]
        public async Task GivenAFeedRequests_WhenExecutedAndFails_ThenTheResponseHeadersStillContainTheRequestCharge()
        {
            _innerClient
                .ReadDatabaseFeedAsync(Arg.Any<FeedOptions>())
                .Throws(CosmosDbMockingHelper.CreateDocumentClientException("error", new NameValueCollection { { CosmosDbHeaders.RequestCharge, "10" } }, (HttpStatusCode?)429));

            await Assert.ThrowsAsync<DocumentClientException>(() => _fhirClient.ReadDatabaseFeedAsync());

            await _cosmosResponseProcessor.Received(1).ProcessException(Arg.Any<DocumentClientException>());
        }
    }
}
