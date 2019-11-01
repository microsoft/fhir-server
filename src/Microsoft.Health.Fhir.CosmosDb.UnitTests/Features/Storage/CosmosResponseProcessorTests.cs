// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Net;
using MediatR;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.CosmosDb.Features.Metrics;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Storage
{
    public class CosmosResponseProcessorTests
    {
        private readonly Dictionary<string, StringValues> _requestHeaders = new Dictionary<string, StringValues>();
        private readonly Dictionary<string, StringValues> _responseHeaders = new Dictionary<string, StringValues>();
        private readonly CosmosResponseProcessor _cosmosResponseProcessor;
        private readonly IMediator _mediator;
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;

        public CosmosResponseProcessorTests()
        {
            _fhirRequestContextAccessor = Substitute.For<IFhirRequestContextAccessor>();
            _fhirRequestContextAccessor.FhirRequestContext.RequestHeaders.Returns(_requestHeaders);
            _fhirRequestContextAccessor.FhirRequestContext.ResponseHeaders.Returns(_responseHeaders);
            _fhirRequestContextAccessor.FhirRequestContext.ResourceType.Returns("resource");
            _fhirRequestContextAccessor.FhirRequestContext.AuditEventType.Returns("operation");

            _mediator = Substitute.For<IMediator>();
            var nullLogger = NullLogger<CosmosResponseProcessor>.Instance;

            _cosmosResponseProcessor = new CosmosResponseProcessor(_fhirRequestContextAccessor, _mediator, nullLogger);
        }

        [Fact]
        public void GivenAResourceResponse_WhenProcessResponseCalled_ThenHeadersShouldBeSetAndMetricNotificationShouldHappen()
        {
            var resourceResponse = Substitute.For<IResourceResponse<Document>>();
            resourceResponse.SessionToken.Returns("2");
            resourceResponse.RequestCharge.Returns(37.37);
            resourceResponse.CollectionSizeUsage.Returns(1234L);
            resourceResponse.StatusCode.Returns(HttpStatusCode.OK);

            _cosmosResponseProcessor.ProcessResponse(resourceResponse);

            ValidateExecution("2", 37.37, 0, 1234L);
        }

        [Fact]
        public void GivenAResourceResponseWith429Status_WhenProcessResponseCalled_ThenThrottledCountShouldBeSet()
        {
            var resourceResponse = Substitute.For<IResourceResponse<Document>>();
            resourceResponse.SessionToken.Returns("2");
            resourceResponse.RequestCharge.Returns(37.37);
            resourceResponse.CollectionSizeUsage.Returns(1234L);
            resourceResponse.StatusCode.Returns(HttpStatusCode.TooManyRequests);

            _cosmosResponseProcessor.ProcessResponse(resourceResponse);

            ValidateExecution("2", 37.37, 1, 1234L);
        }

        [Fact]
        public void GivenAFeedResponse_WhenProcessResponseCalled_ThenHeadersShouldBeSetAndMetricNotificationShouldHappen()
        {
            var feedResponse = Substitute.For<IFeedResponse<Database>>();
            feedResponse.SessionToken.Returns("2");
            feedResponse.RequestCharge.Returns(37.37);
            feedResponse.CollectionSizeUsage.Returns(1234L);

            _cosmosResponseProcessor.ProcessResponse(feedResponse);

            ValidateExecution("2", 37.37, 0, 1234L);
        }

        [Fact]
        public void GivenAStoredProcedureResponse_WhenProcessResponseCalled_ThenHeadersShouldBeSetAndMetricNotificationShouldHappen()
        {
            var storedProcedureResponse = Substitute.For<IStoredProcedureResponse<Database>>();
            storedProcedureResponse.SessionToken.Returns("2");
            storedProcedureResponse.RequestCharge.Returns(37.37);
            storedProcedureResponse.StatusCode.Returns(HttpStatusCode.OK);

            _cosmosResponseProcessor.ProcessResponse(storedProcedureResponse);

            ValidateExecution("2", 37.37, 0, null);
        }

        [Fact]
        public void GivenAFeedResponse_WhenMediatorThrows_ThenExecutionContinues()
        {
            var feedResponse = Substitute.For<IFeedResponse<Database>>();
            feedResponse.SessionToken.Returns("2");
            feedResponse.RequestCharge.Returns(37.37);
            feedResponse.CollectionSizeUsage.Returns(1234L);

            _mediator.Publish(Arg.Any<CosmosStorageRequestMetricsNotification>()).Throws(new Exception("fail"));

            _cosmosResponseProcessor.ProcessResponse(feedResponse);
        }

        [Fact]
        public void GivenAGenericException_WhenProcessing_ThenNothingAdditionalShouldOccur()
        {
            _cosmosResponseProcessor.ProcessException(new Exception("fail"));

            _cosmosResponseProcessor.DidNotReceive().ProcessResponse(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<long?>(), Arg.Any<HttpStatusCode?>());
        }

        [Fact]
        public void GivenADocumentClientExceptionWithNormalStatusCode_WhenProcessing_ThenResponseShouldBeProcessed()
        {
            DocumentClientException documentClientException = CreateDocumentClientException("12.4", "fail", HttpStatusCode.OK);

            _cosmosResponseProcessor.ProcessException(documentClientException);

            _cosmosResponseProcessor.Received().ProcessResponse(null, 12.4, null, HttpStatusCode.OK);
        }

        [Fact]
        public void GivenADocumentClientExceptionWithRequestExceededStatusCode_WhenProcessing_ThenExceptionShouldThrow()
        {
            DocumentClientException documentClientException = CreateDocumentClientException("12.4", "fail", HttpStatusCode.TooManyRequests);

            Assert.Throws<RequestRateExceededException>(() => _cosmosResponseProcessor.ProcessException(documentClientException));

            _cosmosResponseProcessor.Received().ProcessResponse(null, 12.4, null, HttpStatusCode.TooManyRequests);
        }

        [Fact]
        public void GivenADocumentClientExceptionWithSpecificMessage_WhenProcessing_ThenExceptionShouldThrow()
        {
            DocumentClientException documentClientException = CreateDocumentClientException("12.4", "invalid continuation token", HttpStatusCode.OK);

            Assert.Throws<RequestNotValidException>(() => _cosmosResponseProcessor.ProcessException(documentClientException));

            _cosmosResponseProcessor.Received().ProcessResponse(null, 12.4, null, HttpStatusCode.OK);
        }

        [Fact]
        public void GivenNoFhirRequestContext_WhenProcessing_ThenNothingAdditionalShouldOccur()
        {
            _fhirRequestContextAccessor.FhirRequestContext.Returns((IFhirRequestContext)null);
            DocumentClientException documentClientException = CreateDocumentClientException("12.4", "fail", HttpStatusCode.TooManyRequests);

            _cosmosResponseProcessor.ProcessException(documentClientException);

            _cosmosResponseProcessor.DidNotReceive().ProcessResponse(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<long?>(), Arg.Any<HttpStatusCode?>());
        }

        private static DocumentClientException CreateDocumentClientException(string requestCharge, string exceptionMessage, HttpStatusCode httpStatusCode)
        {
            var nameValueCollection = new NameValueCollection();
            nameValueCollection.Add("x-ms-request-charge", requestCharge);
            DocumentClientException documentClientException = CosmosDbMockingHelper.CreateDocumentClientException(exceptionMessage, nameValueCollection, httpStatusCode);
            return documentClientException;
        }

        private void ValidateExecution(string expectedSessionToken, double expectedRequestCharge, int expectedThrottledCount, long? expectedCollectionSizeUsageKilobytes)
        {
            Assert.True(_responseHeaders.TryGetValue(CosmosDbHeaders.SessionToken, out StringValues sessionToken));
            Assert.Equal(expectedSessionToken, sessionToken.ToString());

            Assert.True(_responseHeaders.TryGetValue(CosmosDbHeaders.RequestCharge, out StringValues requestCharge));
            Assert.Equal(expectedRequestCharge.ToString(CultureInfo.InvariantCulture), requestCharge.ToString());

            _mediator.Received(1).Publish(Arg.Is<CosmosStorageRequestMetricsNotification>(c => c.TotalRequestCharge.Equals(expectedRequestCharge)
                                                                                               && c.RequestCount.Equals(1)
                                                                                               && c.ThrottledCount.Equals(expectedThrottledCount)
                                                                                               && c.ResourceType.Equals("resource", StringComparison.InvariantCultureIgnoreCase)
                                                                                               && c.FhirOperation.Equals("operation", StringComparison.InvariantCultureIgnoreCase)
                                                                                               && c.CollectionSizeUsageKilobytes.Equals(expectedCollectionSizeUsageKilobytes)));
        }
    }
}
