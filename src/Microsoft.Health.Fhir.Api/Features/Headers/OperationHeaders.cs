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
    public static class OperationHeaders
    {
        // Generates the url to be included in the response based on the operation and sets the content location header.
        public static OperationResult SetContentLocationHeader(this OperationResult operationResult, IUrlResolver urlResolver, string operationName, string id)
        {
            EnsureArg.IsNotNullOrEmpty(operationName, nameof(operationName));
            EnsureArg.IsNotNullOrEmpty(id, nameof(id));

            var url = urlResolver.ResolveOperationResultUrl(operationName, id);

            operationResult.Headers.Add(HeaderNames.ContentLocation, url.ToString());
            return operationResult;
        }

        public static OperationResult SetContentTypeHeader(this OperationResult oeprationResult, string contentTypeValue)
        {
            EnsureArg.IsNotNullOrEmpty(contentTypeValue);

            oeprationResult.Headers.Add(HeaderNames.ContentType, contentTypeValue);
            return oeprationResult;
        }
    }
}
