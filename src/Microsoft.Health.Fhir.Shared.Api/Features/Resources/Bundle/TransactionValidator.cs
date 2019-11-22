// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Exceptions;
using static Hl7.Fhir.Model.Bundle;

namespace Microsoft.Health.Fhir.Api.Features.Resources.Bundle
{
    public static class TransactionValidator
    {
        // Validates if transaction bundle contains multiple entries that are modifying the same resource.
        public static void ValidateTransactionBundle(Hl7.Fhir.Model.Bundle bundle)
        {
            var resourceIdList = new HashSet<string>(StringComparer.Ordinal);

            foreach (var entry in bundle.Entry)
            {
                if (ShouldValidateBundleEntry(entry))
                {
                    string resourceId = GetResourceUrl(entry);

                    if (!string.IsNullOrEmpty(resourceId))
                    {
                        if (resourceIdList.Contains(resourceId))
                        {
                            throw new RequestNotValidException(string.Format(Api.Resources.ResourcesMustBeUnique, resourceId));
                        }

                        resourceIdList.Add(resourceId);
                    }
                }
            }
        }

        private static bool ShouldValidateBundleEntry(EntryComponent entry)
        {
            string requestUrl = entry.Request.Url;
            HTTPVerb? requestMethod = entry.Request.Method;

            // Search operations using _search and POST endpoint is not supported for bundle.
            if (requestMethod == HTTPVerb.POST && requestUrl.Contains("_search", StringComparison.InvariantCulture))
            {
                throw new RequestNotValidException(string.Format(Api.Resources.InvalidBundleEntry, entry.Request.Url));
            }

            // Check for duplicate resources within a bundle entry is skipped if the entry is bundle or if the request within a entry is not modifying the resource.
            return !(entry.Resource?.ResourceType == Hl7.Fhir.Model.ResourceType.Bundle
                 || requestMethod == HTTPVerb.GET
                 || requestUrl.Contains("$", StringComparison.InvariantCulture));
        }

        private static string GetResourceUrl(EntryComponent component)
        {
            if (component.Request.Method == HTTPVerb.POST)
            {
                return component.FullUrl;
            }

            return component.Request.Url;
        }
    }
}
