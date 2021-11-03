// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Models
{
    internal static class OperationOutcomeConstants
    {
        public static class IssueSeverity
        {
            public const string Error = "Error";
            public const string Warning = "Warning";
            public const string Fatal = "Fatal";
            public const string Information = "Information";
        }

        public static class IssueType
        {
            public const string Forbidden = "Forbidden";
            public const string Security = "Security";
            public const string Required = "Required";
            public const string NotFound = "NotFound";
            public const string Processing = "Processing";
            public const string NotSupported = "NotSupported";
            public const string Invalid = "Invalid";
            public const string Conflict = "Conflict";
            public const string Exception = "Exception";
            public const string Structure = "Structure";
            public const string Incomplete = "Incomplete";
            public const string Informational = "Informational";
            public const string Throttled = "Throttled";
            public const string TooCostly = "TooCostly";
            public const string Timeout = "Timeout";
        }
    }
}
