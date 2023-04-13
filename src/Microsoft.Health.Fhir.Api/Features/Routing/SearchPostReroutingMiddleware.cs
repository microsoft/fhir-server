// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

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
                        var mergedPairs = GetUniqueFormAndQueryStringKeyValues(HttpUtility.ParseQueryString(request.QueryString.ToString()), request.Form);
                        request.Query = mergedPairs;
                    }

                    request.Form = null;
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

        private static QueryCollection GetUniqueFormAndQueryStringKeyValues(NameValueCollection queryCollection, IFormCollection formCollection)
        {
            var uniquePairs = new Dictionary<string, StringValues>();

            foreach (string key in queryCollection.Keys)
            {
                StringValues values = queryCollection.GetValues(key);
                GetUniqueKeyValuesCore(uniquePairs, key, values);
            }

            foreach (var key in formCollection.Keys)
            {
                StringValues values = formCollection[key];
                GetUniqueKeyValuesCore(uniquePairs, key, values);
            }

            return new QueryCollection(uniquePairs);
        }

        private static void GetUniqueKeyValuesCore(Dictionary<string, StringValues> uniquePairs, string key, StringValues values)
        {
            // this discovers the possible multiple values for a key and of course makes sure they're unique
            if (uniquePairs.TryGetValue(key, out StringValues existingValue))
            {
                uniquePairs[key] = values.Union(existingValue).ToArray();
            }
            else
            {
                uniquePairs.Add(key, new StringValues(values.Distinct().ToArray()));
            }
        }
    }
}
