// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using EnsureThat;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Net.Http.Headers;

namespace Microsoft.Health.Fhir.Api.Features.Headers
{
    public static class FhirHeaders
    {
        public static FhirResult SetLocationHeader(this FhirResult fhirResult, IUrlResolver urlResolver)
        {
            var resource = fhirResult.Resource;
            if (!string.IsNullOrEmpty(resource.Id) && !string.IsNullOrWhiteSpace(resource.Meta.VersionId))
            {
                var url = urlResolver.ResolveResourceUrl(resource, true);

                if (url.IsAbsoluteUri)
                {
                    fhirResult.Headers.Add(HeaderNames.Location, url.AbsoluteUri);
                }
            }

            return fhirResult;
        }

        public static FhirResult SetETagHeader(this FhirResult fhirResult)
        {
            var resource = fhirResult.Resource;
            if (resource != null && resource.Meta != null)
            {
                return fhirResult.SetETagHeader(WeakETag.FromVersionId(resource.Meta.VersionId));
            }

            return fhirResult;
        }

        public static FhirResult SetETagHeader(this FhirResult fhirResult, WeakETag weakETag)
        {
            if (weakETag != null)
            {
                fhirResult.Headers.Add(HeaderNames.ETag, weakETag.ToString());
            }

            return fhirResult;
        }

        public static FhirResult SetLastModifiedHeader(this FhirResult fhirResult)
        {
            var resource = fhirResult.Resource;
            if (resource != null)
            {
                if (resource.Meta != null && resource.Meta.LastUpdated.HasValue)
                {
                    fhirResult.Headers.Add(HeaderNames.LastModified, resource.Meta.LastUpdated.Value.ToString("r", CultureInfo.InvariantCulture));
                }
            }

            return fhirResult;
        }

        // Generates the url to be included in the response based on the operation and sets the content location header.
        public static FhirResult SetContentLocationHeader(this FhirResult fhirResult, IUrlResolver urlResolver, string operationName, string id)
        {
            EnsureArg.IsNotNullOrEmpty(operationName, nameof(operationName));
            EnsureArg.IsNotNullOrEmpty(id, nameof(id));

            var url = urlResolver.ResolveOperationResultUrl(operationName, id);

            fhirResult.Headers.Add(HeaderNames.ContentLocation, url.ToString());
            return fhirResult;
        }
    }
}
