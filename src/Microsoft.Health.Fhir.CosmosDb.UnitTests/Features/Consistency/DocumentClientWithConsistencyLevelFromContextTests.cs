// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.CosmosDb.Features.Consistency;
using Newtonsoft.Json;
using NSubstitute;
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

        public DocumentClientWithConsistencyLevelFromContextTests()
        {
            _innerClient = Substitute.For<IDocumentClient>();
            var fhirContextAccessor = Substitute.For<IFhirContextAccessor>();
            fhirContextAccessor.FhirContext.RequestHeaders.Returns(_requestHeaders);
            fhirContextAccessor.FhirContext.ResponseHeaders.Returns(_responseHeaders);

            _consistentClient = new DocumentClientWithConsistencyLevelFromContext(_innerClient, fhirContextAccessor);
        }

        [Fact]
        public async Task GivenACreateRequest_WithSessionConsistency_ThenTheResponseHeadersContainTheSessionToken()
        {
            _innerClient.CreateDocumentAsync(default(string), default).ReturnsForAnyArgs(CreateResourceResponse(new NameValueCollection { { CosmosDbConsistencyHeaders.SessionToken, "2" } }));

            _requestHeaders.Add(CosmosDbConsistencyHeaders.ConsistencyLevel, "Session");
            _requestHeaders.Add(CosmosDbConsistencyHeaders.SessionToken, "1");

            await _consistentClient.CreateDocumentAsync("coll", new { id = 1 });

            Assert.True(_responseHeaders.TryGetValue(CosmosDbConsistencyHeaders.SessionToken, out var values));
            Assert.Equal("2", values.ToString());
        }

        private static ResourceResponse<Document> CreateResourceResponse(NameValueCollection headers)
        {
            Type documentServiceResponseType = typeof(IDocumentClient).Assembly.GetType("Microsoft.Azure.Documents.DocumentServiceResponse");
            var constructor = documentServiceResponseType.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(Stream), typeof(NameValueCollection), typeof(HttpStatusCode), typeof(JsonSerializerSettings) }, null);

            object documentServiceResponse = constructor.Invoke(new object[] { Stream.Null, headers, HttpStatusCode.OK, null });

            var typeConverterType = typeof(IDocumentClient).Assembly.GetType("Microsoft.Azure.Documents.ITypeResolver`1").MakeGenericType(typeof(Document));

            constructor = typeof(ResourceResponse<Document>).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { documentServiceResponseType, typeConverterType }, null);

            return (ResourceResponse<Document>)constructor.Invoke(new object[] { documentServiceResponse, null });
        }

        private static FeedResponse<Document> CreateFeedResponse(NameValueCollection headers)
        {
            var response = new FeedResponse<Document>();
            response.GetType().GetField("responseHeaders", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(response, headers);
            return response;
        }
    }
}
