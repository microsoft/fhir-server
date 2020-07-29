// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Net.Http.Headers;

namespace Microsoft.Health.Fhir.Api.Features.Headers
{
    public static class RawFhirResultExtensions
    {
        public static RawFhirResult SetLocationHeader(this RawFhirResult rawFhirResult, IUrlResolver urlResolver)
        {
            var resource = rawFhirResult.Result;

            //// TODO YAZANAL
            if (!string.IsNullOrEmpty(resource.ResourceId) && !string.IsNullOrWhiteSpace(resource.Version))
            {
                var url = urlResolver.ResolveResourceWrapperUrl(rawFhirResult.Result, true);

                if (url.IsAbsoluteUri)
                {
                    rawFhirResult.Headers.Add(HeaderNames.Location, url.AbsoluteUri);
                }
            }

            return rawFhirResult;
        }

        public static RawFhirResult SetETagHeader(this RawFhirResult rawFhirResult)
        {
            var resource = rawFhirResult.Result;
            if (resource != null)
            {
                return rawFhirResult.SetETagHeader(WeakETag.FromVersionId(resource.Version));
            }

            return rawFhirResult;
        }

        public static RawFhirResult SetETagHeader(this RawFhirResult rawFhirResult, WeakETag weakETag)
        {
            if (weakETag != null)
            {
                rawFhirResult.Headers.Add(HeaderNames.ETag, weakETag.ToString());
            }

            return rawFhirResult;
        }

        public static RawFhirResult SetLastModifiedHeader(this RawFhirResult rawFhirResult)
        {
            ResourceWrapper resource = rawFhirResult.Result;

            var lastUpdated = resource?.LastModified;
            if (lastUpdated != null)
            {
                rawFhirResult.Headers.Add(HeaderNames.LastModified, lastUpdated.Value.ToString("r", CultureInfo.InvariantCulture));
            }

            return rawFhirResult;
        }
    }
}
