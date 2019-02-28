// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net.Mime;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Newtonsoft.Json;

namespace Microsoft.Health.CosmosDb.Features.Health
{
    public static class ResponseWriter
    {
        public static async Task HealthResponseWriter(HttpContext httpContext, HealthReport healthReport)
        {
            var entries = new List<object>();
            foreach (var report in healthReport.Entries)
            {
                var details = new
                {
                    name = report.Key,
                    status = Enum.GetName(typeof(HealthStatus), report.Value.Status),
                    exception = report.Value.Exception,
                };

                entries.Add(details);
            }

            var response = JsonConvert.SerializeObject(
                new
                {
                    overallStatus = healthReport.Status.ToString(),
                    details = entries,
                });

            httpContext.Response.ContentType = MediaTypeNames.Application.Json;
            await httpContext.Response.WriteAsync(response);
        }
    }
}
