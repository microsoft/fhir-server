// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Builder;

namespace Microsoft.Health.Fhir.Api.Features.Exceptions
{
    public static class BaseExceptionMiddlewareExtensions
    {
        public static IApplicationBuilder UseBaseException(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<BaseExceptionMiddleware>();
        }
    }
}
