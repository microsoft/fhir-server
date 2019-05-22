// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Api.Features.Routing
{
    public static class RouteNames
    {
        public const string Metadata = "Metadata";

        public const string ReadResource = "ReadResource";

        public const string ReadResourceWithVersionRoute = "ReadResourceWithVersionRoute";

        public const string SearchResources = "SearchResources";

        public const string SearchAllResources = "SearchAllResources";

        public const string History = "History";

        public const string HistoryType = "HistoryType";

        public const string HistoryTypeId = "HistoryTypeId";

        public const string SearchResourcesPost = "SearchResourcesPost";

        public const string SearchAllResourcesPost = "SearchAllResourcesPost";

        public const string SearchCompartmentByResourceType = "SearchCompartmentByResourceType";

        public const string AadSmartOnFhirProxyAuthorize = "AadSmartOnFhirProxyAuthorize";

        public const string AadSmartOnFhirProxyCallback = "AadSmartOnFhirProxyCallback";

        public const string AadSmartOnFhirProxyToken = "AadSmartOnFhirProxyToken";

        public const string GetExportStatusById = "GetExportStatusById";
    }
}
