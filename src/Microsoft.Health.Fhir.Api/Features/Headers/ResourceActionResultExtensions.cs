// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Net.Http.Headers;

namespace Microsoft.Health.Fhir.Api.Features.Headers
{
    public static class ResourceActionResultExtensions
    {
        // Generates the url to be included in the response based on the operation and sets the content location header.
        public static ResourceActionResult<TResult> SetContentLocationHeader<TResult>(this ResourceActionResult<TResult> result, IUrlResolver urlResolver, string operationName, string id)
        {
            EnsureArg.IsNotNull(result, nameof(result));
            EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));
            EnsureArg.IsNotNullOrWhiteSpace(operationName, nameof(operationName));
            EnsureArg.IsNotNullOrWhiteSpace(id, nameof(id));

            var url = urlResolver.ResolveOperationResultUrl(operationName, id);

            result.Headers[HeaderNames.ContentLocation] = url.ToString();
            return result;
        }

        public static ResourceActionResult<TResult> SetContentTypeHeader<TResult>(this ResourceActionResult<TResult> result, string contentTypeValue)
        {
            EnsureArg.IsNotNull(result, nameof(result));
            EnsureArg.IsNotNullOrWhiteSpace(contentTypeValue, nameof(contentTypeValue));

            result.Headers[HeaderNames.ContentType] = contentTypeValue;
            return result;
        }
    }
}
