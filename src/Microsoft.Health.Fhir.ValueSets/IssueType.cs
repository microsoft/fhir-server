// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.ValueSets
{
    /// <summary>
    /// Value set defined at http://www.hl7.org/fhir/valueset-issue-type.html.
    /// </summary>
    public static class IssueType
    {
        public const string Invalid = "invalid";

        public const string Structure = "structure";

        public const string Required = "required";

        public const string Value = "value";

        public const string Invariant = "invariant";

        public const string Security = "security";

        public const string Login = "login";

        public const string Unknown = "unknown";

        public const string Expired = "expired";

        public const string Forbidden = "forbidden";

        public const string Suppressed = "suppressed";

        public const string Processing = "processing";

        public const string NotSupported = "not-supported";

        public const string Duplicate = "duplicate";

        public const string MultipleMatches = "multiple-matches";

        public const string NotFound = "not-found";

        public const string Deleted = "deleted";

        public const string TooLong = "too-long";

        public const string CodeInvalid = "code-invalid";

        public const string Extension = "extension";

        public const string TooCostly = "too-costly";

        public const string BusinessRule = "business-rule";

        public const string Conflict = "conflict";

        public const string Transient = "transient";

        public const string LockError = "lock-error";

        public const string NoStore = "no-store";

        public const string Exception = "exception";

        public const string Timeout = "timeout";

        public const string Incomplete = "incomplete";

        public const string Throttled = "throttled";

        public const string Informational = "informational";
    }
}
