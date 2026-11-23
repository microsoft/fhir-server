// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Features.ContentTypes;
using Microsoft.Health.Fhir.Api.Features.Partitions;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Partitions;

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

            var endpoint = context.GetEndpoint();
            if (_partitionManager.IsEnabled
                && (!_securityConfiguration.Enabled || context.User?.Identity?.IsAuthenticated == true || endpoint?.Metadata.GetMetadata<AllowAnonymousAttribute>() != null)
                && endpoint?.Metadata.GetMetadata<PartitionIndependentAttribute>() == null)
            {
                string partitionId = context.Request.RouteValues[PartitionRouteMetadata.PartitionId]?.ToString();
                IPartitionInfo partitionInfo = null;
                bool partitionRequired;
                if (partitionId != null)
                {
                    partitionInfo = _partitionManager.TryGetPartition(partitionId, out var partition) ? partition : null;
                    partitionRequired = partitionInfo == null;
                }
                else
                {
                    partitionRequired = endpoint?.Metadata.GetMetadata<PartitionRouteMetadata>() != null;
                }

                if (partitionRequired)
                {
                    _logger.LogWarning("Request rejected: a configured partition ID is required after /partition.");
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    await context.Response.WriteAsJsonAsync(
                        new
                        {
                            resourceType = nameof(OperationOutcome),
                            issue = new[]
                            {
                                    new
                                    {
                                        severity = OperationOutcome.IssueSeverity.Error.GetLiteral(),
                                        code = OperationOutcome.IssueType.NotFound.GetLiteral(),
                                        diagnostics = "A configured partition ID is required after /partition.",
                                    },
                            },
                        },
                        options: null,
                        contentType: KnownContentTypes.JsonContentType,
                        cancellationToken: context.RequestAborted);
                    return;
                }

                fhirRequestContextAccessor.RequestContext.Partition = partitionInfo;

                /*
                if (endpoint?.Metadata.GetMetadata<PartitionRouteMetadata>()?.HasPrefix == true)
                {
                    fhirRequestContextAccessor.RequestContext.BaseUri = new Uri(UriHelper.BuildAbsolute(
                        context.Request.Scheme,
                        context.Request.Host,
                        context.Request.PathBase.Add(PartitionRouteMetadata.GetPrefix(context.Request)),
                        "/"));
                }
                */
            }

            // Call the next delegate/middleware in the pipeline
            await _next(context);
        }
    }
}
