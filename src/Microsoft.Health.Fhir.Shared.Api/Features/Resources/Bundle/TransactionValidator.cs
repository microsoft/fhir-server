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
        public static void ValidateTransaction(HashSet<string> resourceIdList, EntryComponent entry)
        {
            if (entry.Request.Method != HTTPVerb.GET)
            {
                string resourceId = GetResourceUrl(entry);

                if (resourceIdList.Contains(resourceId))
                {
                    throw new RequestNotValidException(string.Format(Api.Resources.ResourcesMustBeUnique, resourceId));
                }

                resourceIdList.Add(resourceId);
            }
        }

        private static string GetResourceUrl(EntryComponent component)
        {
            string fullUrl = component.FullUrl;

            if (fullUrl != null)
            {
                if (fullUrl.StartsWith("urn", StringComparison.OrdinalIgnoreCase) || component.Request.Method == HTTPVerb.POST)
                {
                    return fullUrl;
                }
            }

            return component.Request.Url;
        }
    }
}
