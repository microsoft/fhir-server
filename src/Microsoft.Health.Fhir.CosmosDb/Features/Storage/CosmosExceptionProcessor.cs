// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.CosmosDb.Features.Metrics;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public class CosmosExceptionProcessor : ICosmosExceptionProcessor
    {
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;
        private readonly ICosmosMetricProcessor _cosmosMetricProcessor;

        public CosmosExceptionProcessor(IFhirRequestContextAccessor fhirRequestContextAccessor, ICosmosMetricProcessor cosmosMetricProcessor)
        {
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(cosmosMetricProcessor, nameof(cosmosMetricProcessor));

            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _cosmosMetricProcessor = cosmosMetricProcessor;
        }

        /// <summary>
        /// Adds request charge to the response headers and throws a <see cref="RequestRateExceededException"/>
        /// if the status code is 429.
        /// </summary>
        /// <param name="ex">The exception</param>
        public void ProcessException(Exception ex)
        {
            if (_fhirRequestContextAccessor.FhirRequestContext == null)
            {
                return;
            }

            EnsureArg.IsNotNull(ex, nameof(ex));

            if (ex is DocumentClientException dce)
            {
                _cosmosMetricProcessor.ProcessResponse(null, dce.RequestCharge, null, dce.StatusCode);

                if (dce.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    throw new RequestRateExceededException(dce.RetryAfter);
                }
                else if (dce.Message.Contains("Invalid Continuation Token", StringComparison.OrdinalIgnoreCase))
                {
                    throw new Core.Exceptions.RequestNotValidException(Core.Resources.InvalidContinuationToken);
                }
            }
        }
    }
}
