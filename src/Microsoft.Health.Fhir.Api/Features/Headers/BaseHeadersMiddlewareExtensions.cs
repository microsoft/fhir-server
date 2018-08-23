// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Builder;

namespace Microsoft.Health.Fhir.Api.Features.Headers
{
    public static class BaseHeadersMiddlewareExtensions
    {
        public static IApplicationBuilder UseBaseHeaders(
            this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<BaseHeadersMiddleware>();
        }
    }
}
