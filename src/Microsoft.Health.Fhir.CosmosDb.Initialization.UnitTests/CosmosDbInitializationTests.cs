// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Microsoft.Health.Fhir.CosmosDb.Initialization.Features.Storage.StoredProcedures;
using Xunit;

namespace CosmosDb.Initialization.UnitTests
{
    public class CosmosDbInitializationTests
    {
        [Fact]
        public void Test1()
        {
            var storeProcs = new string[] { "AcquireExportJobsMetadata", "AcquireReindexJobsMetadata", "HardDeleteMetadata", "ReplaceSingleResourceMetadata", "UpdateUnsupportedSearchParametersMetadata" };
            var fhirStoredProcsClasses = typeof(StoredProcedureMetadataBase).Assembly
              .GetTypes().Where(x => !x.IsAbstract && typeof(StoredProcedureMetadataBase).IsAssignableFrom(x))
                .ToArray();
            foreach (var storeproc in fhirStoredProcsClasses)
            {
                Assert.Contains(storeproc.Name, storeProcs);
            }
        }
    }
}
