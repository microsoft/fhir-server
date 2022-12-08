// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.CosmosDb
{
    internal static class Constants
    {
        public const string CollectionConfigurationName = "fhirCosmosDb";
        public const string CosmosDbResponseMessagesProperty = nameof(CosmosDbResponseMessagesProperty);
        public const int ContinuationTokenMinLimit = 1;
        public const int ContinuationTokenMaxLimit = 3;
        public const int ContinuationTokenDefaultLimit = 3;
    }
}
