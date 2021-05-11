// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features
{
    internal static class KnownHeaders
    {
        public const string IfNoneExist = "If-None-Exist";
        public const string PartiallyIndexedParamsHeaderName = "x-ms-use-partial-indices";
        public const string RetryAfterMilliseconds = "x-ms-retry-after-ms";
        public const string RetryAfter = "Retry-After";
        public const string ProvenanceHeader = "X-Provenance";
        public const string Prefer = "Prefer";
        public const string ItemsDeleted = "Items-Deleted";
        public const string RequestId = "X-Request-Id";
        public const string CorrelationId = "X-Correlation-Id";
        public const string EnableChainSearch = "x-ms-enable-chained-search";
        public const string ProfileValidation = "x-ms-profile-validation";
    }
}
