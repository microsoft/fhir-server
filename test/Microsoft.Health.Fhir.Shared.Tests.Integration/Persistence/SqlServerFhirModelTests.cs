// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    public class SqlServerFhirModelTests
    {
        [Fact]
        public async Task GivenANewSqlDatabase_WhenStartingUp_SearchParametersArePopulated()
        {
            // Create a new fixture to ensure we have a new database and that a new instance of SqlServerFhirModel is started.
            var fixture = new SqlServerFhirStorageTestsFixture();

            try
            {
                await fixture.InitializeAsync();

                IReadOnlyCollection<ResourceSearchParameterStatus> expectedStatuses = await fixture.FilebasedSearchParameterRegistry.GetSearchParameterStatuses();
                IReadOnlyCollection<ResourceSearchParameterStatus> actualStatuses = await fixture.SqlServerStatusRegistryDataStore.GetSearchParameterStatuses();

                Assert.Equal(expectedStatuses.Count, actualStatuses.Count);

                var sortedExpected = expectedStatuses.OrderBy(status => status.Uri.ToString()).ToList();
                var sortedActual = actualStatuses.OrderBy(status => status.Uri.ToString()).ToList();

                for (int i = 0; i < expectedStatuses.Count; i++)
                {
                    Assert.Equal(sortedExpected[i].Uri, sortedActual[i].Uri);
                    Assert.Equal(sortedExpected[i].Status, sortedActual[i].Status);
                    Assert.Equal(sortedExpected[i].IsPartiallySupported, sortedActual[i].IsPartiallySupported);
                }
            }
            finally
            {
                await fixture.DisposeAsync();
            }
        }
    }
}
