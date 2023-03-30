// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Api.Features.Routing
{
    internal static class RouteNames
    {
        internal const string Metadata = "Metadata";

        internal const string WellKnownSmartConfiguration = "WellKnownSmartConfiguration";

        internal const string ReadResource = "ReadResource";

        internal const string ReadResourceWithVersionRoute = "ReadResourceWithVersionRoute";

        internal const string SearchResources = "SearchResources";

        internal const string SearchAllResources = "SearchAllResources";

        internal const string History = "History";

        internal const string HistoryType = "HistoryType";

        internal const string HistoryTypeId = "HistoryTypeId";

        internal const string SearchCompartmentByResourceType = "SearchCompartmentByResourceType";

        internal const string AadSmartOnFhirProxyAuthorize = "AadSmartOnFhirProxyAuthorize";

        internal const string AadSmartOnFhirProxyCallback = "AadSmartOnFhirProxyCallback";

        internal const string AadSmartOnFhirProxyToken = "AadSmartOnFhirProxyToken";

        internal const string GetExportStatusById = "GetExportStatusById";

        internal const string CancelExport = "CancelExport";

        internal const string GetReindexStatusById = "GetReindexStatusById";

        internal const string GetImportStatusById = "GetImportStatusById";

        internal const string CancelImport = "CancelImport";

        internal const string PostBundle = "PostBundle";

        internal const string PatientEverythingById = "PatientEverythingById";

        internal const string ReindexOperationDefintion = "ReindexOperationDefintion";

        internal const string ResourceReindexOperationDefinition = "ResourceReindexOperationDefinition";

        internal const string ExportOperationDefinition = "ExportOperationDefinition";

        internal const string PatientExportOperationDefinition = "PatientExportOperationDefinition";

        internal const string GroupExportOperationDefinition = "GroupExportOperationDefinition";

        internal const string AnonymizedExportOperationDefinition = "AnonymizedExportOperationDefinition";

        internal const string ConvertDataOperationDefinition = "ConvertDataOperationDefinition";

        internal const string MemberMatchOperationDefinition = "MemberMatchOperationDefinition";

        internal const string PurgeHistoryDefinition = "PurgeHistoryDefinition";

        internal const string SearchParameterState = "SearchParameterState";

        internal const string PostSearchParameterState = "PostSearchParameterState";

        internal const string SearchParameterStateById = "SearchParameterStateById";
    }
}
