// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.Registry;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Registry
{
    internal static class SearchParameterStatusExtensions
    {
        public static SearchParameterStatusWrapper ToSearchParameterStatusWrapper(this ResourceSearchParameterStatus status)
        {
            return new SearchParameterStatusWrapper
            {
                Uri = status.Uri,
                Status = status.Status,
                LastUpdated = status.LastUpdated,
                IsPartiallySupported = status.IsPartiallySupported ? true : (bool?)null,
            };
        }

        public static ResourceSearchParameterStatus ToSearchParameterStatus(this SearchParameterStatusWrapper wrapper)
        {
            return new ResourceSearchParameterStatus
            {
                Uri = wrapper.Uri,
                Status = wrapper.Status,
                LastUpdated = wrapper.LastUpdated,
                IsPartiallySupported = wrapper.IsPartiallySupported.GetValueOrDefault(),
            };
        }
    }
}
