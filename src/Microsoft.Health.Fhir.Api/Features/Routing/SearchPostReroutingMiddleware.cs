// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using System.Net;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Http;

namespace Microsoft.Health.Fhir.Api.Features.Routing
{
    public class SearchPostReroutingMiddleware
    {
        private readonly RequestDelegate _next;

        public SearchPostReroutingMiddleware(RequestDelegate next)
        {
            EnsureArg.IsNotNull(next);
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            var request = context.Request;

            if (request != null
                && request.Method == "POST"
                && request.Path.Value.EndsWith(KnownRoutes.Search, System.StringComparison.OrdinalIgnoreCase))
            {
                if (request.ContentType is null || request.ContentType == "application/x-www-form-urlencoded")
                {
                    if (request.HasFormContentType)
                    {
                        var dic = request.Query.ToDictionary(k => k.Key, v => v.Value);
                        foreach (var elem in request.Form)
                        {
                            dic.Add(elem.Key, elem.Value);
                        }

                        request.Query = new QueryCollection(dic);
                    }

                    request.Path = request.Path.Value.Substring(0, request.Path.Value.Length - KnownRoutes.Search.Length);
                    request.Method = "GET";
                }
                else
                {
                    context.Response.Clear();
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    await context.Response.WriteAsync(Resources.ContentTypeFormUrlEncodedExpected);
                    return;
                }
            }

            await _next.Invoke(context);
        }
    }
}
