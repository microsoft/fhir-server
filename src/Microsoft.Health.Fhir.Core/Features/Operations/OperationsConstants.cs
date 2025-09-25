// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.ObjectModel;

namespace Microsoft.Health.Fhir.Core.Features.Operations
{
    public static class OperationsConstants
    {
        public const string Operations = "_operations";

        public const string Export = "export";

        public const string PatientExport = "patient-export";

        public const string GroupExport = "group-export";

        public const string ExportContentTypeHeaderValue = "application/json";

        public const string AnonymizedExport = "anonymized-export";

        public const string Reindex = "reindex";

        public const string ResourceReindex = "resource-reindex";

        public const string ReindexContentTypeHeaderValue = "application/json";

        public const string ConvertData = "convert-data";

        public const string MemberMatch = "member-match";

        public const string PatientEverything = "patient-everything";

        public const string PatientEverythingUri = "https://www.hl7.org/fhir/patient-operation-everything.html";

        public const string PurgeHistory = "purge-history";

        public const string Import = "import";

        public const string BulkImportContentTypeHeaderValue = "application/json";

        public const string SearchParameterStatus = "search-parameter-status";

        public const string BulkDelete = "bulk-delete";

        public const string ResourceTypeBulkDelete = "resource-type-bulk-delete";

        public const string BulkDeleteSoftDeleted = "bulk-delete-soft-deleted";

        public const string Includes = "includes";

        public const string BulkUpdate = "bulk-update";

        public const string ResourceTypeBulkUpdate = "resource-type-bulk-update";

        public const string DocRef = "docref";

        public const string ValueSetExpand = $"valueset-expand";

        public static readonly ReadOnlyCollection<string> ExcludedResourceTypesForBulkUpdate = new ReadOnlyCollection<string>(new[] { "SearchParameter", "StructureDefinition" });
    }
}
