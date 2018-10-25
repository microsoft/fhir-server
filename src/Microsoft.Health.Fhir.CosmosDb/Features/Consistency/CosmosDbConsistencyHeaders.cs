// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.CosmosDb.Features.Consistency
{
    internal static class CosmosDbConsistencyHeaders
    {
        public const string ConsistencyLevel = "x-ms-consistency-level";

        public const string SessionToken = "x-ms-session-token";
    }
}
