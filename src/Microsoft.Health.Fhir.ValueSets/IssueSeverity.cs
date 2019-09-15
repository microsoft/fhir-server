// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.ValueSets
{
    /// <summary>
    /// Value set defined at http://www.hl7.org/fhir/valueset-issue-severity.html.
    /// </summary>
    public static class IssueSeverity
    {
        public const string Fatal = "fatal";

        public const string Error = "error";

        public const string Warning = "warning";

        public const string Information = "information";
    }
}
