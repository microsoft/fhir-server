// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Api.Features.Routing
{
    internal static class RouteNames
    {
        internal const string Metadata = nameof(Metadata);

        internal const string WellKnownSmartConfiguration = nameof(WellKnownSmartConfiguration);

        internal const string ReadResource = nameof(ReadResource);

        internal const string ReadResourceWithVersionRoute = nameof(ReadResourceWithVersionRoute);

        internal const string SearchResources = nameof(SearchResources);

        internal const string SearchAllResources = nameof(SearchAllResources);

        internal const string History = nameof(History);

        internal const string HistoryType = nameof(HistoryType);

        internal const string HistoryTypeId = nameof(HistoryTypeId);

        internal const string SearchCompartmentByResourceType = nameof(SearchCompartmentByResourceType);

        internal const string AadSmartOnFhirProxyAuthorize = nameof(AadSmartOnFhirProxyAuthorize);

        internal const string AadSmartOnFhirProxyCallback = nameof(AadSmartOnFhirProxyCallback);

        internal const string AadSmartOnFhirProxyToken = nameof(AadSmartOnFhirProxyToken);

        internal const string GetExportStatusById = nameof(GetExportStatusById);

        internal const string CancelExport = nameof(CancelExport);

        internal const string GetReindexStatusById = nameof(GetReindexStatusById);

        internal const string GetImportStatusById = nameof(GetImportStatusById);

        internal const string CancelImport = nameof(CancelImport);

        internal const string PostBundle = nameof(PostBundle);

        internal const string PatientEverythingById = nameof(PatientEverythingById);

        internal const string ReindexOperationDefintion = nameof(ReindexOperationDefintion);

        internal const string ResourceReindexOperationDefinition = nameof(ResourceReindexOperationDefinition);

        internal const string ExportOperationDefinition = nameof(ExportOperationDefinition);

        internal const string PatientExportOperationDefinition = nameof(PatientExportOperationDefinition);

        internal const string GroupExportOperationDefinition = nameof(GroupExportOperationDefinition);

        internal const string AnonymizedExportOperationDefinition = nameof(AnonymizedExportOperationDefinition);

        internal const string ConvertDataOperationDefinition = nameof(ConvertDataOperationDefinition);

        internal const string MemberMatchOperationDefinition = nameof(MemberMatchOperationDefinition);

        internal const string PurgeHistoryDefinition = nameof(PurgeHistoryDefinition);

        internal const string SearchParameterState = nameof(SearchParameterState);

        internal const string SearchParameterStatusOperationDefinition = "SearchParameterStatusOperationDefinition";

        internal const string PostSearchParameterState = nameof(PostSearchParameterState);

        internal const string UpdateSearchParameterState = nameof(UpdateSearchParameterState);

        internal const string GetBulkDeleteStatusById = nameof(GetBulkDeleteStatusById);

        internal const string CancelBulkDelete = nameof(CancelBulkDelete);

        internal const string BulkDeleteDefinition = nameof(BulkDeleteDefinition);
    }
}
