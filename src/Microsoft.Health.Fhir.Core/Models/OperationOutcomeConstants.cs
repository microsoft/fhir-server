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
            public const string Error = nameof(Error);
            public const string Fatal = nameof(Fatal);
            public const string Information = nameof(Information);
            public const string Warning = nameof(Warning);
        }

        public static class IssueType
        {
            public const string Conflict = nameof(Conflict);
            public const string Duplicated = nameof(Duplicated);
            public const string Exception = nameof(Exception);
            public const string Forbidden = nameof(Forbidden);
            public const string Incomplete = nameof(Incomplete);
            public const string Informational = nameof(Informational);
            public const string Invalid = nameof(Invalid);
            public const string NotFound = nameof(NotFound);
            public const string NotSupported = nameof(NotSupported);
            public const string Processing = nameof(Processing);
            public const string Required = nameof(Required);
            public const string Security = nameof(Security);
            public const string Structure = nameof(Structure);
            public const string Throttled = nameof(Throttled);
            public const string Timeout = nameof(Timeout);
            public const string TooCostly = nameof(TooCostly);
        }
    }
}
