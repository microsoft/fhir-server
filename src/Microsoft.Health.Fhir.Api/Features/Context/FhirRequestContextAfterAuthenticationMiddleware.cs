// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Partitions;
using Microsoft.Health.Fhir.Core.Features.Routing;

namespace Microsoft.Health.Fhir.Api.Features.Context
{
    /// <summary>
    /// Middleware that runs after authentication middleware so that it can retrieved authenticated user claims.
    /// </summary>
    public class FhirRequestContextAfterAuthenticationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IPartitionManager _partitionManager;
        private readonly SecurityConfiguration _securityConfiguration;
        private readonly ILogger<FhirRequestContextAfterAuthenticationMiddleware> _logger;

        private static readonly HashSet<string> ServiceAuthenticationPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "/connect/token",
            "/connect/introspect",
            "/.well-known/openid-configuration",
            "/.well-known/oauth-authorization-server",
            "/.well-known/jwks",
            "/" + KnownRoutes.WellKnownSmartConfiguration,
            "/AadSmartOnFhirProxy/authorize",
            "/AadSmartOnFhirProxy/token",
        };

        public FhirRequestContextAfterAuthenticationMiddleware(
            RequestDelegate next,
            IPartitionManager partitionManager,
            IOptions<SecurityConfiguration> securityConfiguration,
            ILogger<FhirRequestContextAfterAuthenticationMiddleware> logger)
        {
            EnsureArg.IsNotNull(next, nameof(next));
            EnsureArg.IsNotNull(partitionManager, nameof(partitionManager));
            EnsureArg.IsNotNull(securityConfiguration, nameof(securityConfiguration));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _next = next;
            _partitionManager = partitionManager;
            _securityConfiguration = securityConfiguration.Value;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context, RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor)
        {
            // Now the authentication is completed successfully, sets the user.
            if (context.User != null)
            {
                fhirRequestContextAccessor.RequestContext.Principal = context.User;
            }

            // Authentication alone does not challenge anonymous requests. Leave them to authorization
            // without revealing whether a partition exists.
            if (_partitionManager.Partitions.Count > 0
                && (!_securityConfiguration.Enabled || context.User?.Identity?.IsAuthenticated == true))
            {
                var originalPath = (PathString)context.Items[FhirRequestContextMiddleware.OriginalPathKey];
                if (!IsServiceAuthenticationEndpoint(originalPath))
                {
                    PartitionInfo partition = null;
                    bool partitionRequired = false;
                    if (originalPath.StartsWithSegments(new PathString("/partition"), StringComparison.OrdinalIgnoreCase, out PathString afterKeyword))
                    {
                        string partitionId = afterKeyword.Value?.Split('/', 3).ElementAtOrDefault(1);
                        partitionRequired = string.IsNullOrEmpty(partitionId) || !_partitionManager.Partitions.TryGetValue(partitionId, out partition);
                    }
                    else if (_partitionManager.Partitions.Count == 1)
                    {
                        partition = _partitionManager.Partitions.Values.Single();
                    }
                    else if (!originalPath.Equals(new PathString(KnownRoutes.HealthCheck), StringComparison.OrdinalIgnoreCase))
                    {
                        partitionRequired = true;
                    }

                    if (partitionRequired)
                    {
                        _logger.LogWarning("Request rejected: a configured partition ID is required after /partition.");
                        context.Response.StatusCode = StatusCodes.Status404NotFound;
                        context.Response.ContentType = "text/plain";
                        await context.Response.WriteAsync("A configured partition ID is required after /partition.");
                        return;
                    }

                    fhirRequestContextAccessor.RequestContext.PartitionId = partition?.Id;
                }
            }

            // Call the next delegate/middleware in the pipeline
            await _next(context);
        }

        private static bool IsServiceAuthenticationEndpoint(PathString path)
        {
            string value = path.Value?.TrimEnd('/');
            if (ServiceAuthenticationPaths.Contains(value))
            {
                return true;
            }

            const string callbackPrefix = "/AadSmartOnFhirProxy/callback/";
            return value != null
                && value.StartsWith(callbackPrefix, StringComparison.OrdinalIgnoreCase)
                && value.Length > callbackPrefix.Length
                && value.IndexOf('/', callbackPrefix.Length) < 0;
        }
    }
}
