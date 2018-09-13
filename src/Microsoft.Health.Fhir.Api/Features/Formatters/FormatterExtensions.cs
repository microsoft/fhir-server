// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;

namespace Microsoft.Health.Fhir.Api.Features.Formatters
{
    public static class FormatterExtensions
    {
        internal static string GetClosestClientMediaType(this IEnumerable<TextOutputFormatter> outputFormatters, ResourceFormat resourceFormat, IEnumerable<string> acceptHeaders)
        {
            // When overriding the MediaType with the querystring parameter
            // some browsers don't display the response when returning "application/fhir+xml".
            // For this reason we try to match a media type from the OutputFormatter with the request Accept header.

            string closestClientMediaType = null;
            string preferred = ContentType.BuildContentType(resourceFormat, true);
            var preferredMediaType = new MediaType(preferred);

            var selectionContext = new OutputFormatterWriteContext(new DefaultHttpContext(), (stream, encoding) => null, typeof(Resource), null)
            {
                ContentType = $"{preferredMediaType.Type}/{preferredMediaType.SubType}",
            };

            TextOutputFormatter formatter = outputFormatters.FirstOrDefault(x => x.CanWriteResult(selectionContext));

            if (formatter != null)
            {
                closestClientMediaType = acceptHeaders
                    .Intersect(formatter.SupportedMediaTypes)
                    .FirstOrDefault();
            }

            return closestClientMediaType ?? preferred;
        }
    }
}
