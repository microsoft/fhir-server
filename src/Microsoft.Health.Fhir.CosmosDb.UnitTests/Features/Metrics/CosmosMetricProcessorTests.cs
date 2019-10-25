// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using MediatR;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.CosmosDb.Features.Metrics;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Metrics
{
    public class CosmosMetricProcessorTests
    {
        private readonly Dictionary<string, StringValues> _requestHeaders = new Dictionary<string, StringValues>();
        private readonly Dictionary<string, StringValues> _responseHeaders = new Dictionary<string, StringValues>();
        private readonly CosmosMetricProcessor _cosmosMetricProcessor;
        private readonly IMediator _mediator;
        private readonly NullLogger<CosmosMetricProcessor> _nullLogger;

        public CosmosMetricProcessorTests()
        {
            var fhirRequestContextAccessor = Substitute.For<IFhirRequestContextAccessor>();
            fhirRequestContextAccessor.FhirRequestContext.RequestHeaders.Returns(_requestHeaders);
            fhirRequestContextAccessor.FhirRequestContext.ResponseHeaders.Returns(_responseHeaders);
            fhirRequestContextAccessor.FhirRequestContext.ResourceType.Returns("resource");
            fhirRequestContextAccessor.FhirRequestContext.AuditEventType.Returns("operation");

            _mediator = Substitute.For<IMediator>();
            _nullLogger = NullLogger<CosmosMetricProcessor>.Instance;

            _cosmosMetricProcessor = new CosmosMetricProcessor(fhirRequestContextAccessor, _mediator, _nullLogger);
        }

        [Fact]
        public void GivenAResourceResponse_WhenProcessResponseCalled_ThenHeadersShouldBeSetAndMetricNotificationShouldHappen()
        {
            var resourceResponse = Substitute.For<IResourceResponse<Document>>();
            resourceResponse.SessionToken.Returns("2");
            resourceResponse.RequestCharge.Returns(37.37);
            resourceResponse.CollectionSizeUsage.Returns(1234L);
            resourceResponse.StatusCode.Returns(HttpStatusCode.OK);

            _cosmosMetricProcessor.ProcessResponse(resourceResponse);

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

            _cosmosMetricProcessor.ProcessResponse(resourceResponse);

            ValidateExecution("2", 37.37, 1, 1234L);
        }

        [Fact]
        public void GivenAFeedResponse_WhenProcessResponseCalled_ThenHeadersShouldBeSetAndMetricNotificationShouldHappen()
        {
            var feedResponse = Substitute.For<IFeedResponse<Database>>();
            feedResponse.SessionToken.Returns("2");
            feedResponse.RequestCharge.Returns(37.37);
            feedResponse.CollectionSizeUsage.Returns(1234L);

            _cosmosMetricProcessor.ProcessResponse(feedResponse);

            ValidateExecution("2", 37.37, 0, 1234L);
        }

        [Fact]
        public void GivenAStoredProcedureResponse_WhenProcessResponseCalled_ThenHeadersShouldBeSetAndMetricNotificationShouldHappen()
        {
            var storedProcedureResponse = Substitute.For<IStoredProcedureResponse<Database>>();
            storedProcedureResponse.SessionToken.Returns("2");
            storedProcedureResponse.RequestCharge.Returns(37.37);
            storedProcedureResponse.StatusCode.Returns(HttpStatusCode.OK);

            _cosmosMetricProcessor.ProcessResponse(storedProcedureResponse);

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

            _cosmosMetricProcessor.ProcessResponse(feedResponse);
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
