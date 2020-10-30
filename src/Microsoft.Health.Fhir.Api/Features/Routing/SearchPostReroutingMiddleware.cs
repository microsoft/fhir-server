// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Microsoft.Health.Fhir.Api.Features.Routing
{
    public class SearchPostReroutingMiddleware
    {
        private readonly RequestDelegate _next;

        public SearchPostReroutingMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            var request = context.Request;

            if (request.Method == "POST" && request.Path.Value.EndsWith("_search", System.StringComparison.InvariantCulture))
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

                request.Path = request.Path.Value.Substring(0, request.Path.Value.Length - 7);
                request.Method = "GET";
            }

            await _next.Invoke(context);
        }
    }
}
