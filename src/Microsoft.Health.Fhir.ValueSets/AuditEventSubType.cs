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
    }
}
