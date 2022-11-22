// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public static class CosmosDbHeaders
    {
        public const string ConsistencyLevel = "x-ms-consistency-level";

        public const string SessionToken = "x-ms-session-token";

        public const string RequestCharge = "x-ms-request-charge";

        public const string SubStatus = "x-ms-substatus";

        public const string CosmosContinuationTokenSize = "x-ms-documentdb-responsecontinuationtokenlimitinkb";
    }
}
