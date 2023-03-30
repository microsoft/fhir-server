// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    public class ConformanceHealthCheck : IHealthCheck
    {
        private readonly IConformanceProvider _systemConformanceProvider;
        private readonly ILogger<ConformanceHealthCheck> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="CosmosHealthCheck"/> class.
        /// </summary>
        /// <param name="systemConformanceProvider">The provider for the capability statement/</param>
        /// <param name="logger">The logger.</param>
        public ConformanceHealthCheck(
            IConformanceProvider systemConformanceProvider,
            ILogger<ConformanceHealthCheck> logger)
        {
            EnsureArg.IsNotNull(systemConformanceProvider, nameof(systemConformanceProvider));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _systemConformanceProvider = systemConformanceProvider;
            _logger = logger;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                var metadata = await _systemConformanceProvider.GetMetadata(cancellationToken);

                if (metadata != null)
                {
                    return HealthCheckResult.Healthy("Successfully retrieved the capability statement.");
                }
                else
                {
                    _logger.LogError("Capability statement was null.");

                    return HealthCheckResult.Unhealthy("Failed to retrieve the capability statement.");
                }
            }
            catch (ArgumentNullException ex)
            {
                if (ex.StackTrace.Contains("UrlHelperFactory", StringComparison.InvariantCultureIgnoreCase))
                {
                    // If the metadata document hasn't been initialized yet before health check is called the Url Helper factory will not have an action context from this call. The system is still healthy.
                    return HealthCheckResult.Healthy("Successfully retrieved the capability statement.");
                }

                _logger.LogError(ex, "Failed to retrieve the capability statement.");

                return HealthCheckResult.Unhealthy("Failed to retrieve the capability statement.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve the capability statement.");

                return HealthCheckResult.Unhealthy("Failed to retrieve the capability statement.");
            }
        }
    }
}
