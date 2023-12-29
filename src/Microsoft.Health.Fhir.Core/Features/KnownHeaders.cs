// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features
{
    public static class KnownHeaders
    {
        // FHIR Headers
        public const string IfNoneExist = "If-None-Exist";
        public const string ProvenanceHeader = "X-Provenance";
        public const string Progress = "X-Progress";

        // HTTP Headers
        public const string RetryAfter = "Retry-After";
        public const string Prefer = "Prefer";
        public const string RequestId = "X-Request-Id";
        public const string CorrelationId = "X-Correlation-Id";
        public const string InstanceId = "X-Instance-Id";

        // Microsoft Headers (external to our application)
        public const string RetryAfterMilliseconds = "x-ms-retry-after-ms";

        // FHIR Server for Azure Headers
        // Note, the "x-" convention is obsolete: [https://datatracker.ietf.org/doc/html/rfc6648](https://datatracker.ietf.org/doc/html/rfc6648)
        public const string ItemsDeleted = "Items-Deleted";
        public const string PartiallyIndexedParamsHeaderName = "x-ms-use-partial-indices";
        public const string EnableChainSearch = "x-ms-enable-chained-search";
        public const string ProfileValidation = "x-ms-profile-validation";
        public const string CustomAuditHeaderPrefix = "X-MS-AZUREFHIR-AUDIT-";
        public const string FhirUserHeader = "x-ms-fhiruser";

        // #conditionalQueryParallelism - Header used to activate parallel conditional-query processing.
        public const string ConditionalQueryProcessingLogic = "x-conditionalquery-processing-logic";
    }
}
