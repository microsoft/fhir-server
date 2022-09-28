// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Rest;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Crucible
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class CrucibleTestFixture : IClassFixture<CrucibleDataSource>
    {
        private readonly CrucibleDataSource _dataSource;

        public CrucibleTestFixture(CrucibleDataSource dataSource)
        {
            _dataSource = dataSource;
        }

        [Fact(Skip = "These run in background and interfere with other tests. https://github.com/microsoft/fhir-server/issues/1410")]
        [Trait(Traits.Category, Categories.Crucible)]
        public async Task Run()
        {
            await _dataSource.TestRun.Value;
        }
    }
}
