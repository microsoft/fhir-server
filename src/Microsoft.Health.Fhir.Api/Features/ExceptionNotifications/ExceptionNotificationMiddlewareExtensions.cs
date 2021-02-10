// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Builder;

namespace Microsoft.Health.Fhir.Api.Features.ExceptionNotifications
{
    public static class ExceptionNotificationMiddlewareExtensions
    {
        public static IApplicationBuilder UseExceptionNotificationMiddleware(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<ExceptionNotificationMiddleware>();
        }
    }
}
