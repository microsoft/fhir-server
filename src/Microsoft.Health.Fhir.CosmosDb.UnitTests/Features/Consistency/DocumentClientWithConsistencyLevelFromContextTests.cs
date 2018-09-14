// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.CosmosDb.Features.Consistency;
using Newtonsoft.Json;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using Xunit;
using BindingFlags = System.Reflection.BindingFlags;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Consistency
{
    public class DocumentClientWithConsistencyLevelFromContextTests
    {
        private readonly IDocumentClient _innerClient;
        private readonly Dictionary<string, StringValues> _requestHeaders = new Dictionary<string, StringValues>();
        private readonly Dictionary<string, StringValues> _responseHeaders = new Dictionary<string, StringValues>();
        private readonly IDocumentClient _consistentClient;
        private readonly IFhirContextAccessor _fhirContextAccessor;

        public DocumentClientWithConsistencyLevelFromContextTests()
        {
            _innerClient = Substitute.For<IDocumentClient>();
            _fhirContextAccessor = Substitute.For<IFhirContextAccessor>();
            _fhirContextAccessor.FhirContext.RequestHeaders.Returns(_requestHeaders);
            _fhirContextAccessor.FhirContext.ResponseHeaders.Returns(_responseHeaders);

            _consistentClient = new DocumentClientWithConsistencyLevelFromContext(_innerClient, _fhirContextAccessor);
        }

        [Fact]
        public async Task GivenACreateRequest_WithSessionConsistency_ThenTheResponseHeadersContainTheSessionToken()
        {
            _innerClient
                .CreateDocumentAsync("coll", (1, 2), Arg.Is<RequestOptions>(o => o.ConsistencyLevel == ConsistencyLevel.Session))
                .Returns(CreateResourceResponse(new Document(), HttpStatusCode.OK, new NameValueCollection { { CosmosDbConsistencyHeaders.SessionToken, "2" } }));

            _requestHeaders.Add(CosmosDbConsistencyHeaders.ConsistencyLevel, "Session");
            _requestHeaders.Add(CosmosDbConsistencyHeaders.SessionToken, "1");

            await _consistentClient.CreateDocumentAsync("coll", (1, 2));

            Assert.True(_responseHeaders.TryGetValue(CosmosDbConsistencyHeaders.SessionToken, out var values));
            Assert.Equal("2", values.ToString());
        }

        [Fact]
        public async Task GivenACreateRequest_WithWithNoConsistencySpecifiedAndNoRequestOptions_ThenNoRequestOptionsAreCreated()
        {
            _innerClient.CreateDocumentAsync("coll", (1, 2)).Returns(CreateResourceResponse(new Document(), HttpStatusCode.OK, new NameValueCollection()));
            await _consistentClient.CreateDocumentAsync("coll", (1, 2));
            await _innerClient.Received().CreateDocumentAsync("coll", (1, 2));
        }

        [Fact]
        public async Task GivenACreateRequest_WithNoFhirContext_ThenNoRequestOptionsAreCreated()
        {
            _fhirContextAccessor.FhirContext.ReturnsNull();
            _innerClient.CreateDocumentAsync("coll", (1, 2)).Returns(CreateResourceResponse(new Document(), HttpStatusCode.OK, new NameValueCollection()));
            await _consistentClient.CreateDocumentAsync("coll", (1, 2));
            await _innerClient.Received().CreateDocumentAsync("coll", (1, 2));
        }

        [Fact]
        public async Task GivenAFeedRequest_WithSessionConsistency_ThenTheResponseHeadersContainTheSessionToken()
        {
            _innerClient
                .ReadDatabaseFeedAsync(Arg.Is<FeedOptions>(o => o.ConsistencyLevel == ConsistencyLevel.Session))
                .ReturnsForAnyArgs(CreateFeedResponse(Enumerable.Empty<Database>(), new NameValueCollection { { CosmosDbConsistencyHeaders.SessionToken, "2" } }));

            _requestHeaders.Add(CosmosDbConsistencyHeaders.ConsistencyLevel, "Session");
            _requestHeaders.Add(CosmosDbConsistencyHeaders.SessionToken, "1");

            await _consistentClient.ReadDatabaseFeedAsync();

            Assert.True(_responseHeaders.TryGetValue(CosmosDbConsistencyHeaders.SessionToken, out var values));
            Assert.Equal("2", values.ToString());
        }

        [Fact]
        public async Task GivenAFeedRequest_WithWithNoConsistencySpecifiedAndNoRequestOptions_ThenNoRequestOptionsAreCreated()
        {
            _innerClient.ReadDatabaseFeedAsync()
                .ReturnsForAnyArgs(CreateFeedResponse(Enumerable.Empty<Database>(), new NameValueCollection()));
            await _consistentClient.ReadDatabaseFeedAsync();
            await _innerClient.Received().ReadDatabaseFeedAsync();
        }

        [Fact]
        public async Task GivenAFeedRequest_WithAnUnrecognizedConsistencyLevelHeaderValue_ThenABadRequestExceptionIsThrown()
        {
            _requestHeaders.Add(CosmosDbConsistencyHeaders.ConsistencyLevel, "CatsAndDogs");

            await Assert.ThrowsAsync<BadRequestException>(() => _consistentClient.ReadDatabaseFeedAsync());
        }

        [Fact]
        public async Task GivenAStoredProcedureRequestWithLink_WithSessionConsistency_ThenTheResponseHeadersContainTheSessionToken()
        {
            _innerClient
                .ExecuteStoredProcedureAsync<int>("link", Arg.Is<RequestOptions>(o => o.ConsistencyLevel == ConsistencyLevel.Session))
                .Returns(CreateStoredProcedureResponse(42, HttpStatusCode.OK, new NameValueCollection { { CosmosDbConsistencyHeaders.SessionToken, "2" } }));

            _requestHeaders.Add(CosmosDbConsistencyHeaders.ConsistencyLevel, "Session");
            _requestHeaders.Add(CosmosDbConsistencyHeaders.SessionToken, "1");

            await _consistentClient.ExecuteStoredProcedureAsync<int>("link");

            Assert.True(_responseHeaders.TryGetValue(CosmosDbConsistencyHeaders.SessionToken, out var values));
            Assert.Equal("2", values.ToString());
        }

        [Fact]
        public async Task GivenAStoredProcedureRequestWithUri_WithSessionConsistency_ThenTheResponseHeadersContainTheSessionToken()
        {
            _innerClient
                .ExecuteStoredProcedureAsync<int>(default(Uri), Arg.Is<RequestOptions>(o => o.ConsistencyLevel == ConsistencyLevel.Session))
                .Returns(CreateStoredProcedureResponse(42, HttpStatusCode.OK, new NameValueCollection { { CosmosDbConsistencyHeaders.SessionToken, "2" } }));

            _requestHeaders.Add(CosmosDbConsistencyHeaders.ConsistencyLevel, "Session");
            _requestHeaders.Add(CosmosDbConsistencyHeaders.SessionToken, "1");

            await _consistentClient.ExecuteStoredProcedureAsync<int>(default(Uri));

            Assert.True(_responseHeaders.TryGetValue(CosmosDbConsistencyHeaders.SessionToken, out var values));
            Assert.Equal("2", values.ToString());
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
            _requestHeaders.Add(CosmosDbConsistencyHeaders.ConsistencyLevel, requestedConsistencyLevel.ToString());

            await Assert.ThrowsAsync<BadRequestException>(() => _consistentClient.ReadDatabaseFeedAsync());
        }

#pragma warning disable SA1124 // Do not use regions
        #region ugly reflection as a workaround for the SDK not being mock-friendly

        private static ResourceResponse<T> CreateResourceResponse<T>(T resource, HttpStatusCode statusCode, NameValueCollection responseHeaders)
            where T : Resource, new()
        {
            return (ResourceResponse<T>)CreateInstance( // internal ResourceResponse(DocumentServiceResponse response, ITypeResolver<TResource> typeResolver = null)
                typeof(ResourceResponse<T>),
                CreateDocumentServiceResponse(resource, statusCode, responseHeaders),
                null);
        }

        private static StoredProcedureResponse<T> CreateStoredProcedureResponse<T>(T resource, HttpStatusCode statusCode, NameValueCollection responseHeaders)
        {
            return (StoredProcedureResponse<T>)CreateInstance( // internal StoredProcedureResponse(DocumentServiceResponse response, JsonSerializerSettings serializerSettings = null)
                typeof(StoredProcedureResponse<T>),
                CreateDocumentServiceResponse(resource, statusCode, responseHeaders),
                null);
        }

        private static object CreateDocumentServiceResponse<T>(T resource, HttpStatusCode statusCode, NameValueCollection responseHeaders)
        {
            var serializer = new JsonSerializer();
            var ms = new MemoryStream();
            var jsonTextWriter = new JsonTextWriter(new StreamWriter(ms));
            serializer.Serialize(jsonTextWriter, resource);
            jsonTextWriter.Flush();
            ms.Position = 0;

            return CreateInstance( // internal DocumentServiceResponse(Stream body, INameValueCollection headers, HttpStatusCode statusCode, JsonSerializerSettings serializerSettings = null)
                typeof(IDocumentClient).Assembly.GetType("Microsoft.Azure.Documents.DocumentServiceResponse"),
                ms,
                CreateInstance( // public DictionaryNameValueCollection(NameValueCollection c)
                    typeof(IDocumentClient).Assembly.GetType("Microsoft.Azure.Documents.Collections.DictionaryNameValueCollection"),
                    responseHeaders),
                statusCode,
                null);
        }

        private static object CreateInstance(Type type, params object[] args) => Activator.CreateInstance(type, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public, null, args, CultureInfo.InvariantCulture);

        private static FeedResponse<T> CreateFeedResponse<T>(IEnumerable<T> result, NameValueCollection responseHeaders)
        {
            return (FeedResponse<T>)CreateInstance( // internal FeedResponse(IEnumerable<T> result, int count, INameValueCollection responseHeaders, bool useETagAsContinuation = false, IReadOnlyDictionary<string, Microsoft.Azure.Documents.QueryMetrics> queryMetrics = null, ClientSideRequestStatistics requestStats = null, string disallowContinuationTokenMessage = null)
                typeof(FeedResponse<T>),
                result,
                result.Count(),
                CreateInstance( // public DictionaryNameValueCollection(NameValueCollection c)
                    typeof(IDocumentClient).Assembly.GetType("Microsoft.Azure.Documents.Collections.DictionaryNameValueCollection"),
                    responseHeaders),
                false,
                null,
                null,
                null);
        }

        #endregion
#pragma warning restore SA1124 // Do not use regions
    }
}
