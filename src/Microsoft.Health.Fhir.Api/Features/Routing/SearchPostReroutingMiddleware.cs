// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Routing;

namespace Microsoft.Health.Fhir.Api.Features.Routing
{
    public class SearchPostReroutingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<SearchPostReroutingMiddleware> _logger;

        public SearchPostReroutingMiddleware(RequestDelegate next, ILogger<SearchPostReroutingMiddleware> logger)
        {
            EnsureArg.IsNotNull(next);
            _next = next;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context)
        {
            var request = context.Request;

            try
            {
                if (request != null
                    && request.Method == "POST"
                    && request.Path.Value.EndsWith(KnownRoutes.Search, System.StringComparison.OrdinalIgnoreCase))
                {
                    if (request.ContentType is null || request.HasFormContentType)
                    {
                        _logger.LogInformation("Rerouting POST to GET with query parameters from form body.");

                        if (request.HasFormContentType)
                        {
                            var mergedPairs = GetUniqueFormAndQueryStringKeyValues(HttpUtility.ParseQueryString(request.QueryString.ToString()), request.Form);
                            request.Query = mergedPairs;
                        }

                        request.ContentType = null;
                        request.Form = null;
                        request.Path = request.Path.Value.Substring(0, request.Path.Value.Length - KnownRoutes.Search.Length);
                        request.Method = "GET";
                    }
                    else
                    {
                        _logger.LogDebug("Rejecting POST with invalid Content-Type.");

                        context.Response.Clear();
                        context.Response.StatusCode = (int)HttpStatusCode.BadRequest;

                        var operationOutcome = new OperationOutcome
                        {
                            Id = Guid.NewGuid().ToString(),
                            Issue = new List<OperationOutcome.IssueComponent>()
                        {
                            new OperationOutcome.IssueComponent()
                            {
                                Severity = OperationOutcome.IssueSeverity.Error,
                                Code = OperationOutcome.IssueType.Invalid,
                                Diagnostics = Api.Resources.ContentTypeFormUrlEncodedExpected,
                            },
                        },
                            Meta = new Meta()
                            {
                                LastUpdated = Clock.UtcNow,
                            },
                        };

                        await context.Response.WriteAsJsonAsync(operationOutcome);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while rerouting POST search to GET.");
                throw;
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
