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
    public static class BundleValidator
    {
        public static void ValidateTransactionBundle(Hl7.Fhir.Model.Bundle bundle)
        {
            var resourceIdList = new HashSet<string>();

            foreach (var component in bundle.Entry)
            {
                // the rule applies to POST, PUT and DELETE methods only.
                if (component.Request.Method != HTTPVerb.GET)
                {
                    string resourceId = GetResourceId(component);

                    if (resourceIdList.Contains(resourceId))
                    {
                        throw new RequestNotValidException(string.Format(Api.Resources.ResourcesMustBeUnique, resourceId));
                    }

                    resourceIdList.Add(resourceId);
                }
            }
        }

        private static string GetResourceId(EntryComponent component)
        {
            string fullUrl = component.FullUrl;

            if (fullUrl != null)
            {
                if (fullUrl.StartsWith("urn", StringComparison.OrdinalIgnoreCase))
                {
                    return fullUrl;
                }
                else
                {
                    return component.Request.Url;
                }
            }

            return component.Request.Url;
        }
    }
}
