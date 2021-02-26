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
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features;

namespace Microsoft.Health.Fhir.Api.Features.Formatters
{
    public static class HttpContextExtensions
    {
        public static SummaryType GetSummaryType(this HttpContext context, ILogger logger)
        {
            var query = context.Request.Query[KnownQueryParameterNames.Summary].FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(query) && context.Response.StatusCode == (int)HttpStatusCode.OK)
            {
                if (!Enum.TryParse<SummaryType>(query, true, out var summary))
                {
                    return summary;
                }
            }

            return SummaryType.False;
        }

        public static string[] GetElementsSearchParameter(this HttpContext context, ILogger logger)
        {
            var query = context.Request.Query[KnownQueryParameterNames.Elements].FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(query) && context.Response.StatusCode == (int)HttpStatusCode.OK)
            {
                var elements = query.Split(new char[1] { ',' });

                logger.LogDebug("Setting elements to return: '{0}'", string.Join(", ", elements));

                return elements;
            }

            return null;
        }

        public static bool GetIsPretty(this HttpContext context)
        {
            var query = context.Request.Query[KnownQueryParameterNames.Pretty].FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(query))
            {
                if (!bool.TryParse(query, out bool isPretty))
                {
                    // ContentTypeService validates the _pretty parameter. This is reached if other errors are encountered when parsing the query string.
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
