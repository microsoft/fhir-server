// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage.StoredProcedures;
using Xunit;

namespace Microsoft.Health.Fhir.CosmosDb.Initialization.UnitTests
{
    public class CosmosDbInitializationTests
    {
        [Fact]
        public void GivenList_ThenStoreProcedresshouldreturnValidList()
        {
            var storeProcs = new string[] { "AcquireExportJobsMetadata", "AcquireReindexJobsMetadata", "HardDeleteMetadata", "ReplaceSingleResourceMetadata", "UpdateUnsupportedSearchParametersMetadata" };
            var fhirStoredProcsClasses = typeof(StoredProcedureMetadataBase).Assembly
              .GetTypes()
              .Where(x => !x.IsAbstract && typeof(StoredProcedureMetadataBase).IsAssignableFrom(x))
              .ToArray();
            foreach (var storeproc in fhirStoredProcsClasses)
            {
               Assert.Contains(storeproc.Name, storeProcs);
            }
        }
    }
}
