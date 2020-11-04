// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.ValueSets
{
    /// <summary>
    /// Value set defined at https://www.hl7.org/fhir/valueset-audit-event-sub-type.html
    /// </summary>
    public static class AuditEventSubType
    {
        public const string System = "http://hl7.org/fhir/ValueSet/audit-event-sub-type";

        public const string Create = "create";

        public const string Read = "read";

        public const string VRead = "vread";

        public const string Update = "update";

        public const string Delete = "delete";

        public const string History = "history";

        public const string HistoryType = "history-type";

        public const string HistorySystem = "history-system";

        public const string HistoryInstance = "history-instance";

        public const string Search = "search";

        public const string SearchType = "search-type";

        public const string SearchSystem = "search-system";

        public const string Capabilities = "capabilities";

        public const string SmartOnFhirAuthorize = "smart-on-fhir-authorize";

        public const string SmartOnFhirCallback = "smart-on-fhir-callback";

        public const string SmartOnFhirToken = "smart-on-fhir-token";

        public const string BundlePost = "bundle-post";

        public const string Batch = "batch";

        public const string Transaction = "transaction";

        public const string Patch = "patch";

        public const string Operation = "operation";

        // The spec has an "operation" audit-event-sub-type, but that only refers to an operation
        // that is defined by an OperationDefinition. And export does not fall under that list as
        // of 2019/03/19. So we have to use our own sub-type.
        public const string Export = "export";

        public const string Reindex = "reindex";
    }
}
