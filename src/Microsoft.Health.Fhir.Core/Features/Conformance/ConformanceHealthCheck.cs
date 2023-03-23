// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    public class ConformanceHealthCheck : IHealthCheck
    {
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;
        private readonly IHttpClientFactory _clientFactory;
        private readonly ILogger<ConformanceHealthCheck> _logger;

        public ConformanceHealthCheck(
            RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor,
            IHttpClientFactory clientFactory,
            ILogger<ConformanceHealthCheck> logger)
        {
            EnsureArg.IsNotNull(logger, nameof(logger));
            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _clientFactory = clientFactory;
            _logger = logger;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                using var client = _clientFactory.CreateClient();
                var response = await client.GetAsync(new Uri(_fhirRequestContextAccessor.RequestContext.BaseUri, "metadata"), cancellationToken);

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return HealthCheckResult.Healthy("Successfully retrieved the capability statement.");
                }
                else
                {
                    _logger.LogWarning("Capability statement status was {Status}.", response.StatusCode);

                    return HealthCheckResult.Unhealthy("Failed to retrieve the capability statement.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to retrieve the capability statement.");

                return HealthCheckResult.Unhealthy("Failed to retrieve the capability statement.");
            }
        }
    }
}
