// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.CosmosDb.Features.Metrics;
using Microsoft.Health.Fhir.CosmosDb.Features.Queries;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Storage
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class CosmosResponseProcessorTests
    {
        private readonly Dictionary<string, StringValues> _requestHeaders = new Dictionary<string, StringValues>();
        private readonly Dictionary<string, StringValues> _responseHeaders = new Dictionary<string, StringValues>();
        private readonly CosmosResponseProcessor _cosmosResponseProcessor;
        private readonly IMediator _mediator;
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;

        public CosmosResponseProcessorTests()
        {
            _fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            _fhirRequestContextAccessor.RequestContext.RequestHeaders.Returns(_requestHeaders);
            _fhirRequestContextAccessor.RequestContext.ResponseHeaders.Returns(_responseHeaders);
            _fhirRequestContextAccessor.RequestContext.ResourceType.Returns("resource");
            _fhirRequestContextAccessor.RequestContext.AuditEventType.Returns("operation");

            _mediator = Substitute.For<IMediator>();
            var nullLogger = NullLogger<CosmosResponseProcessor>.Instance;
            _cosmosResponseProcessor = new CosmosResponseProcessor(_fhirRequestContextAccessor, _mediator, Substitute.For<ICosmosQueryLogger>(), nullLogger);
        }

        [Fact]
        public async Task GivenAResourceResponse_WhenProcessResponseCalled_ThenHeadersShouldBeSetAndMetricNotificationShouldHappen()
        {
            Headers headers = Substitute.ForPartsOf<Headers>();
            headers.Session.Returns("2");
            headers.RequestCharge.Returns(37.37);

            ResponseMessage responseMessage = Substitute.ForPartsOf<ResponseMessage>(HttpStatusCode.OK, null, null);
            responseMessage.Headers.Returns(headers);

            await _cosmosResponseProcessor.ProcessResponse(responseMessage);
            ValidateExecution("2", 37.37, false);
        }

        [Fact]
        public async Task GivenAResourceResponseWith429Status_WhenProcessResponseCalled_ThenThrottledCountShouldBeSet()
        {
            Headers headers = Substitute.ForPartsOf<Headers>();
            headers.Session.Returns("2");
            headers.RequestCharge.Returns(37.37);

            ResponseMessage responseMessage = Substitute.ForPartsOf<ResponseMessage>(HttpStatusCode.TooManyRequests, null, null);
            responseMessage.Headers.Returns(headers);

            await _cosmosResponseProcessor.ProcessResponse(responseMessage);
            ValidateExecution("2", 37.37, true);
        }

        [Fact]
        public async Task GivenAnExceptionWithNormalStatusCode_WhenProcessing_ThenResponseShouldBeProcessed()
        {
            ResponseMessage response = CreateResponseException("fail", HttpStatusCode.OK);

            await _cosmosResponseProcessor.ProcessErrorResponse(response);
        }

        [Fact]
        public async Task GivenAnExceptionWithRequestExceededStatusCode_WhenProcessing_ThenExceptionShouldThrow()
        {
            ResponseMessage response = CreateResponseException("fail", HttpStatusCode.TooManyRequests);

            await Assert.ThrowsAsync<RequestRateExceededException>(async () => await _cosmosResponseProcessor.ProcessErrorResponse(response));
        }

        [Fact]
        public async Task GivenAnExceptionWithSpecificMessage_WhenProcessing_ThenExceptionShouldThrow()
        {
            ResponseMessage response = CreateResponseException("invalid continuation token", HttpStatusCode.BadRequest);

            await Assert.ThrowsAsync<RequestNotValidException>(async () => await _cosmosResponseProcessor.ProcessErrorResponse(response));
        }

        [Theory]
        [InlineData(KnownCosmosDbCmkSubStatusValue.AadClientCredentialsGrantFailure)]
        [InlineData(KnownCosmosDbCmkSubStatusValue.AadServiceUnavailable)]
        [InlineData(KnownCosmosDbCmkSubStatusValue.KeyVaultAuthenticationFailure)]
        [InlineData(KnownCosmosDbCmkSubStatusValue.KeyVaultKeyNotFound)]
        [InlineData(KnownCosmosDbCmkSubStatusValue.KeyVaultServiceUnavailable)]
        [InlineData(KnownCosmosDbCmkSubStatusValue.KeyVaultWrapUnwrapFailure)]
        [InlineData(KnownCosmosDbCmkSubStatusValue.InvalidKeyVaultKeyUri)]
        [InlineData(KnownCosmosDbCmkSubStatusValue.KeyVaultInternalServerError)]
        [InlineData(KnownCosmosDbCmkSubStatusValue.KeyVaultDnsNotResolved)]
        public async Task GivenAnExceptionWithCmkSubStatus_WhenProcessing_ThenExceptionShouldThrow(KnownCosmosDbCmkSubStatusValue subStatusValue)
        {
            ResponseMessage response = CreateResponseException("fail", HttpStatusCode.Forbidden, Convert.ToString((int)subStatusValue));

            await Assert.ThrowsAsync<CustomerManagedKeyException>(async () => await _cosmosResponseProcessor.ProcessErrorResponse(response));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("3999")]
        public async Task GivenAnExceptionWithForbiddenStatusCodeAndUnknownSubStatus_WhenProcessing_ThenNothingElseShouldOccur(string subsStatusCode)
        {
            ResponseMessage response = CreateResponseException("fail", HttpStatusCode.Forbidden, subsStatusCode);

            await _cosmosResponseProcessor.ProcessErrorResponse(response);
        }

        [Fact]
        public async Task GivenAThrottlingResponseWithRetryAfterHeader_WhenProcessed_ThrowsWithRetryAfter()
        {
            var retryAfter = TimeSpan.FromMilliseconds(200);

            RequestRateExceededException exception = await Assert.ThrowsAsync<RequestRateExceededException>(async () => await _cosmosResponseProcessor.ProcessErrorResponse(HttpStatusCode.TooManyRequests, new Headers { { "x-ms-retry-after-ms", ((int)retryAfter.TotalMilliseconds).ToString() } }, "too many requests"));
            Assert.Equal(retryAfter, exception.RetryAfter);
        }

        [Fact]
        public async Task GivenAThrottlingResponseWithoutRetryAfterHeader_WhenProcessed_ThrowsWithoutRetryAfter()
        {
            RequestRateExceededException exception = await Assert.ThrowsAsync<RequestRateExceededException>(async () => await _cosmosResponseProcessor.ProcessErrorResponse(HttpStatusCode.TooManyRequests, new Headers(), "too many requests"));
            Assert.Null(exception.RetryAfter);
        }

        private static ResponseMessage CreateResponseException(string exceptionMessage, HttpStatusCode httpStatusCode, string subStatus = null)
        {
            var message = new ResponseMessage(httpStatusCode, errorMessage: exceptionMessage);

            if (subStatus != null)
            {
                message.Headers.Add(CosmosDbHeaders.SubStatus, subStatus);
            }

            return message;
        }

        private void ValidateExecution(string expectedSessionToken, double expectedRequestCharge, bool expectedThrottled)
        {
            if (expectedSessionToken != null)
            {
                Assert.True(_responseHeaders.TryGetValue(CosmosDbHeaders.SessionToken, out StringValues sessionToken));
                Assert.Equal(expectedSessionToken, sessionToken.ToString());
            }

            Assert.True(_responseHeaders.TryGetValue(CosmosDbHeaders.RequestCharge, out StringValues requestCharge));
            Assert.Equal(expectedRequestCharge.ToString(CultureInfo.InvariantCulture), requestCharge.ToString());

            _mediator.Received(1).Publish(Arg.Is<CosmosStorageRequestMetricsNotification>(c => c.TotalRequestCharge.Equals(expectedRequestCharge)
                                                                                               && c.IsThrottled.Equals(expectedThrottled)
                                                                                               && c.ResourceType.Equals("resource", StringComparison.InvariantCultureIgnoreCase)
                                                                                               && c.FhirOperation.Equals("operation", StringComparison.InvariantCultureIgnoreCase)));
        }
    }
}
