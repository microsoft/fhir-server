// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    /// <summary>
    /// Provides STU3 specific tests.
    /// </summary>
    public partial class VersionSpecificTests : IClassFixture<HttpIntegrationTestFixture>
    {
        [Fact]
        public async Task GivenStu3Server_WhenCapabilityStatementIsRetrieved_ThenCorrectVersionShouldBeReturned()
        {
            await TestCapabilityStatementFhirVersion("3.0.1");
        }
    }
}
