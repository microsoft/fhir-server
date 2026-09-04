// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Operations
{
    public static class IntegrationDataStoreClientConstants
    {
        public const string BlobPropertyETag = "ETag";
        public const string BlobPropertyLength = "Length";

        /// <summary>
        /// The reserved URI scheme identifying the in-memory synthetic import source used for CPU-only
        /// $import measurements. Only recognized when
        /// <see cref="Configs.IntegrationDataStoreConfiguration.EnableTestSourceOverride"/> is <c>true</c>.
        /// See InMemoryTestIntegrationDataSource in Microsoft.Health.Fhir.Azure for the implementation.
        /// </summary>
        public const string InMemoryTestSourceScheme = "inmemorytest";
    }
}
