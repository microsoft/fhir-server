// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using Hl7.Fhir.Rest;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Health.Fhir.Core.Features;

namespace Microsoft.Health.Fhir.Api.Features.Formatters
{
    public static class HttpContextExtensions
    {
        public static SummaryType GetSummaryTypeOrDefault(this HttpContext context)
        {
            var query = context.Request.Query[KnownQueryParameterNames.Summary].FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(query) &&
                (context.Response.StatusCode == (int)HttpStatusCode.OK || context.Response.StatusCode == (int)HttpStatusCode.Created) &&
                Enum.TryParse<SummaryType>(query, true, out var summary))
            {
                return summary;
            }
            else if (string.IsNullOrWhiteSpace(query))
            {
                var res = context.Request.Query[KnownQueryParameterNames.Count].FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(res) && Convert.ToInt32(res) == 0 &&
                    (context.Response.StatusCode == (int)HttpStatusCode.OK || context.Response.StatusCode == (int)HttpStatusCode.Created))
                {
                    return Hl7.Fhir.Rest.SummaryType.Count;
                }
            }

            return SummaryType.False;
        }

        public static string[] GetElementsOrDefault(this HttpContext context)
        {
            var query = context.Request.Query[KnownQueryParameterNames.Elements].FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(query) &&
                (context.Response.StatusCode == (int)HttpStatusCode.OK || context.Response.StatusCode == (int)HttpStatusCode.Created))
            {
                var elements = query.Split(new char[1] { ',' });
                return elements;
            }

            return null;
        }

        public static bool GetPrettyOrDefault(this HttpContext context)
        {
            var query = context.Request.Query[KnownQueryParameterNames.Pretty].FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(query))
            {
                if (!bool.TryParse(query, out bool isPretty))
                {
                    isPretty = default;
                }

                return isPretty;
            }

            return false;
        }

        public static void AllowSynchronousIO(this HttpContext context)
        {
            var bodyControlFeature = context.Features.Get<IHttpBodyControlFeature>();
            if (bodyControlFeature != null)
            {
                bodyControlFeature.AllowSynchronousIO = true;
            }
        }
    }
}
