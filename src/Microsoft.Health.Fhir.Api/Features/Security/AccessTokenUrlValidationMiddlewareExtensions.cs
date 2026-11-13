// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.AspNetCore.Builder;

namespace Microsoft.Health.Fhir.Api.Features.Security
{
    public static class AccessTokenUrlValidationMiddlewareExtensions
    {
        public static IApplicationBuilder UseAccessTokenUrlValidation(
            this IApplicationBuilder builder)
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            return builder.UseMiddleware<AccessTokenUrlValidationMiddleware>();
        }
    }
}
