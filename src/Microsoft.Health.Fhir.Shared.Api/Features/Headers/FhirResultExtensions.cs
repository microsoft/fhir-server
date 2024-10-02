// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Net.Http.Headers;

namespace Microsoft.Health.Fhir.Api.Features.Headers
{
    public static class FhirResultExtensions
    {
        public static FhirResult SetLocationHeader(this FhirResult fhirResult, IUrlResolver urlResolver)
        {
            var resource = fhirResult.Result;

            if (!string.IsNullOrEmpty(resource.Id) && !string.IsNullOrWhiteSpace(resource.VersionId))
            {
                var url = urlResolver.ResolveResourceUrl(fhirResult.Result, true);

                if (url.IsAbsoluteUri)
                {
                    fhirResult.Headers[HeaderNames.Location] = url.AbsoluteUri;
                }
            }

            return fhirResult;
        }

        public static FhirResult SetETagHeader(this FhirResult fhirResult)
        {
            var resource = fhirResult.Result;
            if (resource != null)
            {
                return fhirResult.SetETagHeader(WeakETag.FromVersionId(resource.VersionId));
            }

            return fhirResult;
        }

        public static FhirResult SetETagHeader(this FhirResult fhirResult, WeakETag weakETag)
        {
            if (weakETag != null)
            {
                fhirResult.Headers[HeaderNames.ETag] = weakETag.ToString();
            }

            return fhirResult;
        }

        public static FhirResult SetLastModifiedHeader(this FhirResult fhirResult)
        {
            IResourceElement resource = fhirResult.Result;

            DateTimeOffset? lastUpdated = resource?.LastUpdated;

            if (lastUpdated != null)
            {
                fhirResult.Headers[HeaderNames.LastModified] = lastUpdated.Value.ToString("r", CultureInfo.InvariantCulture);
            }

            return fhirResult;
        }
    }
}
