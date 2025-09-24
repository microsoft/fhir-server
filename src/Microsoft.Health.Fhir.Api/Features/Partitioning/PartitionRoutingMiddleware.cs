// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Partitioning;

namespace Microsoft.Health.Fhir.Api.Features.Partitioning
{
    /// <summary>
    /// Middleware that handles partition routing by extracting partition names from URLs
    /// and rewriting them to standard FHIR paths while storing partition context.
    /// </summary>
    public class PartitionRoutingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<PartitionRoutingMiddleware> _logger;

        public PartitionRoutingMiddleware(
            RequestDelegate next,
            ILogger<PartitionRoutingMiddleware> logger)
        {
            _next = EnsureArg.IsNotNull(next, nameof(next));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        public async Task InvokeAsync(
            HttpContext context,
            RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor,
            IPartitionService partitionService)
        {
            EnsureArg.IsNotNull(context, nameof(context));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(partitionService, nameof(partitionService));

            var path = context.Request.Path;
            string partitionName = null;

            try
            {
                if (path.StartsWithSegments("/partitions", StringComparison.OrdinalIgnoreCase))
                {
                    // Extract partition from URL: /partitions/{name}/...
                    var segments = path.Value.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    if (segments.Length >= 2)
                    {
                        partitionName = segments[1]; // partitionName from URL

                        // Validate partition name format (64 chars, alphanumeric + . - _)
                        if (!IsValidPartitionName(partitionName))
                        {
                            _logger.LogWarning("Invalid partition name format: {PartitionName}", partitionName);
                            context.Response.StatusCode = 400;
                            await context.Response.WriteAsync($"Invalid partition name format: '{partitionName}'. " +
                                "Partition names must be 1-64 characters containing only letters, numbers, dots, dashes, and underscores.");
                            return;
                        }

                        // Set PathBase to include partition prefix for URL generation
                        var partitionBase = $"/partitions/{partitionName}";
                        context.Request.PathBase = context.Request.PathBase.Add(partitionBase);

                        // Rewrite path by removing /partitions/{name} prefix
                        var newPath = "/" + string.Join("/", segments.Skip(2));
                        if (string.IsNullOrEmpty(newPath) || newPath == "/")
                        {
                            // Handle root partition requests
                            newPath = "/";
                        }

                        context.Request.Path = newPath;

                        _logger.LogDebug(
                            "Set PathBase to {PathBase} and rewritten path from {OriginalPath} to {NewPath} for partition {PartitionName}",
                            context.Request.PathBase,
                            path.Value,
                            newPath,
                            partitionName);
                    }
                }
                else
                {
                    // Non-partitioned requests → 'default' partition
                    partitionName = "default";
                }

                // Store partition context in existing FhirRequestContext
                var fhirContext = fhirRequestContextAccessor.RequestContext;
                if (fhirContext != null)
                {
                    fhirContext.PartitionName = partitionName;

                    // Get partition ID from service
                    if (partitionService.IsPartitioningEnabled())
                    {
                        try
                        {
                            fhirContext.LogicalPartitionId = await partitionService.GetPartitionIdAsync(partitionName);
                        }
                        catch (ResourceNotFoundException)
                        {
                            // Partition doesn't exist - create it implicitly
                            _logger.LogInformation("Creating new partition: {PartitionName}", partitionName);
                            fhirContext.LogicalPartitionId = await partitionService.CreatePartitionAsync(partitionName);
                        }
                    }

                    _logger.LogDebug(
                        "Set partition context: Name={PartitionName}, Id={PartitionId}",
                        partitionName,
                        fhirContext.LogicalPartitionId);
                }

                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing partition routing for path {Path}", path.Value);
                throw;
            }
        }

        /// <summary>
        /// Validates partition name according to Azure DICOM specification:
        /// 1-64 characters, alphanumeric plus dots, dashes, and underscores.
        /// </summary>
        /// <param name="name">The partition name to validate.</param>
        /// <returns>True if valid, false otherwise.</returns>
        private static bool IsValidPartitionName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 64)
            {
                return false;
            }

            // Check for reserved names
            if (IsReservedPartitionName(name))
            {
                return false;
            }

            // Validate characters: alphanumeric + . - _
            return name.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_');
        }

        /// <summary>
        /// Checks if the partition name is reserved and should not be used by users.
        /// </summary>
        /// <param name="name">The partition name to check.</param>
        /// <returns>True if reserved, false otherwise.</returns>
        private static bool IsReservedPartitionName(string name)
        {
            var reservedNames = new[] { "system", "admin", "metadata" };
            return reservedNames.Contains(name, StringComparer.OrdinalIgnoreCase);
        }
    }
}
