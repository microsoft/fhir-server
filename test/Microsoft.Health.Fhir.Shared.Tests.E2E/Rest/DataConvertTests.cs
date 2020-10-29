// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.DataConvert;
using Microsoft.Health.Fhir.Core.Features.Operations.DataConvert.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Fhir.Tests.E2E.Rest;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Tests.E2E.Rest
{
    [Trait(Traits.Category, Categories.DataConvert)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class DataConvertTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private bool _isUsingInProcTestServer = false;
        private readonly TestFhirClient _testFhirClient;
        private readonly IContainerRegistryTokenProvider _containerRegistryTokenProvider;
        private readonly DataConvertConfiguration _dataConvertConfiguration;

        public DataConvertTests(HttpIntegrationTestFixture<StartupForAnonymizedExportTestProvider> fixture)
        {
            _isUsingInProcTestServer = fixture.IsUsingInProcTestServer;
            _testFhirClient = fixture.TestFhirClient;
            _dataConvertConfiguration = ((IOptions<DataConvertConfiguration>)(fixture.TestFhirServer as InProcTestFhirServer)?.Server?.Services?.GetService(typeof(IOptions<ExportJobConfiguration>)))?.Value;
        }

        private async Task<string> GetRegistryToken()
        {
            ContainerRegistryInfo registry = _dataConvertConfiguration.ContainerRegistries.First();
            if (string.IsNullOrEmpty(registry.ContainerRegistryUsername)
                || string.IsNullOrEmpty(registry.ContainerRegistryPassword))
            {
                registry.ContainerRegistryUsername = registry.ContainerRegistryServer.Split('.')[0];
                registry.ContainerRegistryPassword = Environment.GetEnvironmentVariable(registry.ContainerRegistryUsername + "_secret");
            }

            return await _containerRegistryTokenProvider.GetTokenAsync(registry, CancellationToken.None);
        }
    }
}
