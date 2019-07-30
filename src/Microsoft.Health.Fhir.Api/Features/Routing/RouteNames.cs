// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Api.Features.Routing
{
    internal static class RouteNames
    {
        internal const string Metadata = "Metadata";

        internal const string ReadResource = "ReadResource";

        internal const string ReadResourceWithVersionRoute = "ReadResourceWithVersionRoute";

        internal const string SearchResources = "SearchResources";

        internal const string SearchAllResources = "SearchAllResources";

        internal const string History = "History";

        internal const string HistoryType = "HistoryType";

        internal const string HistoryTypeId = "HistoryTypeId";

        internal const string SearchResourcesPost = "SearchResourcesPost";

        internal const string SearchAllResourcesPost = "SearchAllResourcesPost";

        internal const string SearchCompartmentByResourceType = "SearchCompartmentByResourceType";

        internal const string AadSmartOnFhirProxyAuthorize = "AadSmartOnFhirProxyAuthorize";

        internal const string AadSmartOnFhirProxyCallback = "AadSmartOnFhirProxyCallback";

        internal const string AadSmartOnFhirProxyToken = "AadSmartOnFhirProxyToken";

        internal const string GetExportStatusById = "GetExportStatusById";

        internal const string CancelExport = "CancelExport";
    }
}
