// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.ValueSets
{
    /// <summary>
    /// Value set defined at https://www.hl7.org/fhir/valueset-audit-event-type.html
    /// </summary>
    public static class AuditEventType
    {
        public const string System = "http://hl7.org/fhir/audit-event-type";

        public const string RestFulOperationCode = "rest";
    }
}
