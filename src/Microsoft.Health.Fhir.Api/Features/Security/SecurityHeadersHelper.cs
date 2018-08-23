// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Http;

namespace Microsoft.Health.Fhir.Api.Features.Security
{
    internal static class SecurityHeadersHelper
    {
        private const string XContentTypeOptions = "X-Content-Type-Options";
        private const string XContentTypeOptionsValue = "nosniff";

        internal static Task SetSecurityHeaders(object context)
        {
            EnsureArg.IsNotNull(context, nameof(context));
            EnsureArg.IsTrue(context is HttpContext, nameof(context));
            var httpContext = (HttpContext)context;

            httpContext.Response.Headers.TryAdd(XContentTypeOptions, XContentTypeOptionsValue);

            return Task.CompletedTask;
        }
    }
}
