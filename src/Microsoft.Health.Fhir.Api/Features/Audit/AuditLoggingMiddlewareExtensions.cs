// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.AspNetCore.Builder;
using Microsoft.Health.Fhir.Api.Features.Audit;

namespace Microsoft.AspNetCore.Builder
{
    /// <summary>
    /// Extension methods for registering audit logging middleware.
    /// </summary>
    public static class AuditLoggingMiddlewareExtensions
    {
        /// <summary>
        /// Adds the audit logging middleware to ensure HTTP 405 responses are audit logged.
        /// </summary>
        /// <param name="builder">The application builder.</param>
        /// <returns>The application builder.</returns>
        public static IApplicationBuilder UseAuditLoggingMiddleware(this IApplicationBuilder builder)
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            return builder.UseMiddleware<AuditLoggingMiddleware>();
        }
    }
}