// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Microsoft.Azure.Cosmos.Scripts;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage.StoredProcedures;
using Microsoft.Health.Fhir.CosmosDb.Initialization.Features.Storage.StoredProcedures;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.CosmosDb.Initialization.UnitTests
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class CosmosDbInitializationTests
    {
        [Fact]
        public void GivenAListOfExpectedStoredProcedures_ThenAllStoredProceduresInTheAssemblyShouldMatch()
        {
            string[] storeProcs = new string[]
            {
                "AcquireReindexJobsMetadata",
                "HardDeleteMetadata",
                "ReplaceSingleResourceMetadata",
                "UpdateUnsupportedSearchParametersMetadata",
            };

            Type[] fhirStoredProcsClasses = typeof(DataPlaneStoredProcedureInstaller).Assembly
              .GetTypes()
              .Where(x => !x.IsAbstract && typeof(StoredProcedureMetadataBase).IsAssignableFrom(x))
              .ToArray();

            Assert.NotEmpty(fhirStoredProcsClasses);

            foreach (string sp in storeProcs)
            {
                Assert.Contains(sp, fhirStoredProcsClasses.Select(x => x.Name));
            }
        }

        [Fact]
        public void GivenAllStoredProceduresInTheAssembly_ThenAllStructureShouldBeAsExpected()
        {
            Type[] fhirStoredProcsClasses = typeof(DataPlaneStoredProcedureInstaller).Assembly
              .GetTypes()
              .Where(x => !x.IsAbstract && typeof(StoredProcedureMetadataBase).IsAssignableFrom(x))
              .ToArray();

            Assert.NotEmpty(fhirStoredProcsClasses);

            foreach (Type storeproc in fhirStoredProcsClasses)
            {
                StoredProcedureMetadataBase storedProcedureMetadata = storeproc.GetConstructors().First().Invoke(null) as StoredProcedureMetadataBase;

                Assert.NotNull(storedProcedureMetadata);
                Assert.NotNull(storedProcedureMetadata.FullName);

                StoredProcedureProperties metadataProperties = storedProcedureMetadata.ToStoredProcedureProperties();
                Assert.NotNull(metadataProperties.Id);
                Assert.False(string.IsNullOrEmpty(metadataProperties.Body));

                string storedProcedureHash = metadataProperties.Body.ComputeHash();
                Assert.Equal($"{storedProcedureMetadata.Name}_{storedProcedureHash}", storedProcedureMetadata.FullName);
            }
        }
    }
}
