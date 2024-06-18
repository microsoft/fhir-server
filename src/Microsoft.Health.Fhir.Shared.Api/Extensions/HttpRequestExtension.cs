// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Health.Fhir.Core.Features.Telemetry;

namespace Microsoft.Health.Fhir.Api.Extensions
{
    public static class HttpRequestExtension
    {
        public static string GetOperationName(this HttpRequest request, bool includeRouteValues = true)
        {
            if (request != null)
            {
                var name = request.Path.Value;
                if (request.RouteValues != null
                    && request.RouteValues.TryGetValue(KnownHttpRequestProperties.RouteValueAction, out var action)
                    && request.RouteValues.TryGetValue(KnownHttpRequestProperties.RouteValueController, out var controller))
                {
                    name = $"{controller}/{action}";

                    if (includeRouteValues)
                    {
                        var parameterArray = request.RouteValues.Keys?.Where(
                            k => k.Contains(KnownHttpRequestProperties.RouteValueParameterSuffix, StringComparison.OrdinalIgnoreCase))
                            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                            .ToArray();
                        if (parameterArray != null && parameterArray.Any())
                        {
                            name += $" [{string.Join("/", parameterArray)}]";
                        }
                    }
                }

                return $"{request.Method} {name}".Trim();
            }

            return null;
        }
    }
}
