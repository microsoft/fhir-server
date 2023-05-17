// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using OperationsConstants = Microsoft.Health.Fhir.Core.Features.Operations.OperationsConstants;

namespace Microsoft.Health.Fhir.Api.Features.Routing
{
    internal class KnownRoutes
    {
        internal const string ResourceTypeRouteConstraint = "fhirResource";
        internal const string CompartmentResourceTypeRouteConstraint = "fhirCompartmentResource";
        internal const string CompartmentTypeRouteConstraint = "fhirCompartment";

        private const string ResourceTypeRouteSegment = "{" + KnownActionParameterNames.ResourceType + ":" + ResourceTypeRouteConstraint + "}";
        private const string CompartmentResourceTypeRouteSegment = "{" + KnownActionParameterNames.ResourceType + ":" + CompartmentResourceTypeRouteConstraint + "}";
        private const string CompartmentTypeRouteSegment = "{" + KnownActionParameterNames.CompartmentType + ":" + CompartmentTypeRouteConstraint + "}";
        private const string IdRouteSegment = "{" + KnownActionParameterNames.Id + "}";
        private const string VidRouteSegment = "{" + KnownActionParameterNames.Vid + "}";

        public const string History = "_history";
        public const string Search = "_search";
        public const string ResourceType = ResourceTypeRouteSegment;
        public const string ResourceTypeHistory = ResourceType + "/" + History;
        public const string ResourceTypeSearch = ResourceType + "/" + Search;
        public const string ResourceTypeById = ResourceType + "/" + IdRouteSegment;
        public const string ResourceTypeByIdHistory = ResourceTypeById + "/" + History;
        public const string ResourceTypeByIdAndVid = ResourceTypeByIdHistory + "/" + VidRouteSegment;

        public const string OperationDefinition = "OperationDefinition";

        public const string Export = "$export";
        public const string ExportResourceType = ResourceType + "/" + Export;
        public const string ExportResourceTypeById = ResourceTypeById + "/" + Export;
        public const string ExportJobLocation = OperationsConstants.Operations + "/" + OperationsConstants.Export + "/" + IdRouteSegment;
        public const string ExportOperationDefinition = OperationDefinition + "/" + OperationsConstants.Export;
        public const string PatientExportOperationDefinition = OperationDefinition + "/" + OperationsConstants.PatientExport;
        public const string GroupExportOperationDefinition = OperationDefinition + "/" + OperationsConstants.GroupExport;
        public const string AnonymizedExportOperationDefinition = OperationDefinition + "/" + OperationsConstants.AnonymizedExport;

        public const string Validate = "$validate";
        public const string ValidateResourceType = ResourceType + "/" + Validate;
        public const string ValidateResourceTypeById = ResourceTypeById + "/" + Validate;

        public const string Reindex = "$reindex";
        public const string ReindexSingleResource = ResourceTypeById + "/" + Reindex;
        public const string ReindexJobLocation = OperationsConstants.Operations + "/" + OperationsConstants.Reindex + "/" + IdRouteSegment;
        public const string ReindexOperationDefinition = OperationDefinition + "/" + OperationsConstants.Reindex;
        public const string ResourceReindexOperationDefinition = OperationDefinition + "/" + OperationsConstants.ResourceReindex;

        public const string ConvertData = "$convert-data";
        public const string ConvertDataOperationDefinition = OperationDefinition + "/" + OperationsConstants.ConvertData;

        public const string MemberMatch = "Patient/$member-match";
        public const string MemberMatchOperationDefinition = OperationDefinition + "/" + OperationsConstants.MemberMatch;

        public const string Everything = "$everything";
        public const string PatientEverythingById = "Patient/" + IdRouteSegment + "/" + Everything;

        public const string PurgeHistory = "$purge-history";
        public const string PurgeHistoryResourceTypeById = ResourceTypeById + "/" + PurgeHistory;
        public const string PurgeHistoryOperationDefinition = OperationDefinition + "/" + OperationsConstants.PurgeHistory;

        public const string Import = "$import";
        public const string ImportDataOperationDefinition = OperationDefinition + "/" + OperationsConstants.Import;
        public const string ImportJobLocation = OperationsConstants.Operations + "/" + OperationsConstants.Import + "/" + IdRouteSegment;

        public const string CompartmentTypeByResourceType = CompartmentTypeRouteSegment + "/" + IdRouteSegment + "/" + CompartmentResourceTypeRouteSegment;

        public const string WellKnown = ".well-known";
        public const string SmartConfiguration = "smart-configuration";
        public const string WellKnownSmartConfiguration = WellKnown + "/" + SmartConfiguration;

        public const string Metadata = "metadata";
        public const string Versions = "$versions";

        public const string HealthCheck = "/health/check";
        public const string CustomError = "/CustomError";

        public const string SearchParameters = "SearchParameter/";
        public const string Status = "$status";
        public const string SearchParametersStatusQuery = SearchParameters + Status;
        public const string SearchParametersStatusById = SearchParameters + IdRouteSegment + "/" + Status;
        public const string SearchParametersStatusPostQuery = SearchParametersStatusQuery + "/" + Search;

        public const string BulkDelete = "$bulk-delete";
        public const string BulkDeleteResourceType = ResourceType + "/" + BulkDelete;
        public const string BulkDeleteJobLocation = OperationsConstants.Operations + "/" + OperationsConstants.BulkDelete + "/" + IdRouteSegment;
        public const string BulkDeleteOperationDefinition = OperationDefinition + "/" + OperationsConstants.BulkDelete;
        public const string ResourceTypeBulkDeleteOperationDefinition = OperationDefinition + "/" + OperationsConstants.ResourceTypeBulkDelete;
    }
}
