// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Specialized;
using System.Net;
using Microsoft.Azure.Documents;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.CosmosDb.Features.Metrics;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Storage
{
    public class CosmosExceptionProcessorTests
    {
        private IFhirRequestContextAccessor _fhirRequestContextAccessor;
        private CosmosExceptionProcessor _cosmosExceptionProcessor;
        private ICosmosMetricProcessor _cosmosMetricProcessor;

        public CosmosExceptionProcessorTests()
        {
            _fhirRequestContextAccessor = Substitute.For<IFhirRequestContextAccessor>();
            _fhirRequestContextAccessor.FhirRequestContext.Returns(Substitute.For<IFhirRequestContext>());
            _cosmosMetricProcessor = Substitute.For<ICosmosMetricProcessor>();

            _cosmosExceptionProcessor = new CosmosExceptionProcessor(_fhirRequestContextAccessor, _cosmosMetricProcessor);
        }

        [Fact]
        public void GivenAGenericException_WhenProcessing_ThenNothingAdditionalShouldOccur()
        {
            _cosmosExceptionProcessor.ProcessException(new Exception("fail"));

            _cosmosMetricProcessor.DidNotReceive().ProcessResponse(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<long?>(), Arg.Any<HttpStatusCode?>());
        }

        [Fact]
        public void GivenADocumentClientExceptionWithNormalStatusCode_WhenProcessing_ThenResponseShouldBeProcessed()
        {
            DocumentClientException documentClientException = CreateDocumentClientException("12.4", "fail", HttpStatusCode.OK);

            _cosmosExceptionProcessor.ProcessException(documentClientException);

            _cosmosMetricProcessor.Received().ProcessResponse(null, 12.4, null, HttpStatusCode.OK);
        }

        [Fact]
        public void GivenADocumentClientExceptionWithRequestExceededStatusCode_WhenProcessing_ThenExceptionShouldThrow()
        {
            DocumentClientException documentClientException = CreateDocumentClientException("12.4", "fail", HttpStatusCode.TooManyRequests);

            Assert.Throws<RequestRateExceededException>(() => _cosmosExceptionProcessor.ProcessException(documentClientException));

            _cosmosMetricProcessor.Received().ProcessResponse(null, 12.4, null, HttpStatusCode.TooManyRequests);
        }

        [Fact]
        public void GivenADocumentClientExceptionWithSpecificMessage_WhenProcessing_ThenExceptionShouldThrow()
        {
            DocumentClientException documentClientException = CreateDocumentClientException("12.4", "invalid continuation token", HttpStatusCode.OK);

            Assert.Throws<RequestNotValidException>(() => _cosmosExceptionProcessor.ProcessException(documentClientException));

            _cosmosMetricProcessor.Received().ProcessResponse(null, 12.4, null, HttpStatusCode.OK);
        }

        [Fact]
        public void GivenNoFhirRequestContext_WhenProcessing_ThenNothingAdditionalShouldOccur()
        {
            _fhirRequestContextAccessor.FhirRequestContext.Returns((IFhirRequestContext)null);
            DocumentClientException documentClientException = CreateDocumentClientException("12.4", "fail", HttpStatusCode.TooManyRequests);

            _cosmosExceptionProcessor.ProcessException(documentClientException);

            _cosmosMetricProcessor.DidNotReceive().ProcessResponse(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<long?>(), Arg.Any<HttpStatusCode?>());
        }

        private static DocumentClientException CreateDocumentClientException(string requestCharge, string exceptionMessage, HttpStatusCode httpStatusCode)
        {
            var nameValueCollection = new NameValueCollection();
            nameValueCollection.Add("x-ms-request-charge", requestCharge);
            DocumentClientException documentClientException = CosmosDbMockingHelper.CreateDocumentClientException(exceptionMessage, nameValueCollection, httpStatusCode);
            return documentClientException;
        }
    }
}
