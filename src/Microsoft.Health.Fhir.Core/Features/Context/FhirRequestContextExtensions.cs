// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;

namespace Microsoft.Health.Fhir.Core.Features.Context
{
    public static class FhirRequestContextExtensions
    {
        public static DateTimeOffset? GetSearchParameterLastUpdated(this IFhirRequestContext context)
        {
            if (context?.Properties.TryGetValue(SearchParameterRequestContextPropertyNames.LastUpdated, out var value) == true)
            {
                return (DateTimeOffset)value;
            }

            return null;
        }

        public static void SetSearchParameterLastUpdated(this IFhirRequestContext context, DateTimeOffset? lastUpdated)
        {
            if (lastUpdated.HasValue && context != null)
            {
                context.Properties[SearchParameterRequestContextPropertyNames.LastUpdated] = lastUpdated.Value;
            }
        }

        public static void ClearSearchParameterLastUpdated(this IFhirRequestContext context)
        {
            context?.Properties.Remove(SearchParameterRequestContextPropertyNames.LastUpdated);
        }
    }
}
