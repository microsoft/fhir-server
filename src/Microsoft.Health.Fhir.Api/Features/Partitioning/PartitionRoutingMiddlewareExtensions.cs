// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.AspNetCore.Builder;

namespace Microsoft.Health.Fhir.Api.Features.Partitioning
{
    /// <summary>
    /// Extension methods for registering partition routing middleware.
    /// </summary>
    public static class PartitionRoutingMiddlewareExtensions
    {
        /// <summary>
        /// Registers the partition routing middleware that extracts partition names from URLs
        /// and rewrites them to standard FHIR paths while storing partition context.
        /// This middleware should be registered before routing middleware.
        /// </summary>
        /// <param name="builder">The application builder.</param>
        /// <returns>The application builder for chaining.</returns>
        public static IApplicationBuilder UsePartitionRouting(this IApplicationBuilder builder)
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            return builder.UseMiddleware<PartitionRoutingMiddleware>();
        }
    }
}
