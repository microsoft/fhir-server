// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Fhir.Api.Features.ContentTypes;

namespace Microsoft.Health.Fhir.Api.Features.Formatters
{
    public static class FormatterExtensions
    {
        private static readonly Dictionary<ResourceFormat, string> ResourceFormatContentType = new Dictionary<ResourceFormat, string>
        {
            { ResourceFormat.Json, KnownContentTypes.JsonContentType },
            { ResourceFormat.Xml, KnownContentTypes.XmlContentType },
        };

        internal static string GetClosestClientMediaType(this IEnumerable<TextOutputFormatter> outputFormatters, string contentType, IEnumerable<string> acceptHeaders)
        {
            // When overriding the MediaType with the query string parameter
            // some browsers don't display the response when returning "application/fhir+xml".
            // For this reason we try to match a media type from the OutputFormatter with the request Accept header.

            string closestClientMediaType = null;

            if (outputFormatters != null && acceptHeaders != null)
            {
                // Gets formatters that can write the desired format
                var validFormatters = outputFormatters
                    .Where(x => x.GetSupportedContentTypes(contentType, typeof(Resource)) != null)
                    .ToArray();

                var acceptHeadersArray = acceptHeaders.ToArray();

                // Using the valid formatters, select the correct content type header for the client
                closestClientMediaType = acceptHeadersArray
                    .SelectMany(x => validFormatters.SelectMany(y => y.GetSupportedContentTypes(x, typeof(Resource)) ?? Enumerable.Empty<string>()))
                    .Distinct()
                    .Intersect(acceptHeadersArray)
                    .FirstOrDefault();
            }

            return closestClientMediaType ?? contentType;
        }

        internal static string ToContentType(this ResourceFormat resourceType)
        {
            if (ResourceFormatContentType.TryGetValue(resourceType, out string contentType))
            {
                return contentType;
            }

            throw new UnsupportedMediaTypeException(Api.Resources.UnsupportedFormatParameter);
        }
    }
}
