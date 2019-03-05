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
    public static class HealthCheckResponseWriter
    {
        public static async Task WriteJson(HttpContext httpContext, HealthReport healthReport)
        {
            var allEntries = new List<object>();
            foreach (var reportEntry in healthReport.Entries)
            {
                var individualEntry = new
                {
                    name = reportEntry.Key,
                    status = Enum.GetName(typeof(HealthStatus), reportEntry.Value.Status),
                    exception = reportEntry.Value.Exception,
                };

                allEntries.Add(individualEntry);
            }

            var response = JsonConvert.SerializeObject(
                new
                {
                    overallStatus = healthReport.Status.ToString(),
                    details = allEntries,
                });

            httpContext.Response.ContentType = MediaTypeNames.Application.Json;
            await httpContext.Response.WriteAsync(response);
        }
    }
}
