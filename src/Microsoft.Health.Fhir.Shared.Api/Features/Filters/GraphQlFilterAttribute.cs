// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Api.Features.Filters
{
    public sealed class GraphQlFilterAttribute : ActionFilterAttribute
    {
        private JsonSerializerSettings jsonSerializerSettings;

        public GraphQlFilterAttribute()
        {
            jsonSerializerSettings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };
        }

        public override async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
        {
            // When a Result is executed, I can look at their request path and if it contains graphQL
            if (context.Result is FhirResult result && context.HttpContext.Request.Path.Value?.IndexOf("$graphql", StringComparison.Ordinal) > -1)
            {
                // This will be run on top of the result because we need to search results first
                /*
                    var scheme = new Schema { Query = new PatientType() };
                    var runResult = await executor.ExecuteAsync(_ =>
                    {
                        _.Schema = schema;
                        _.Query = context.HttpContext.Request.Query["query"];
                        _.Root = result.Resource;
                    });
                */

                // This will go and get serialized later on
                context.Result = new ObjectResult(new { prop = "Hello" });
            }

            await next();
        }
    }
}
