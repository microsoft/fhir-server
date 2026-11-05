// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
    public class ResourceIdLengthTests :
        IClassFixture<HttpIntegrationTestFixture<ResourceIdLengthTests.StandardIdStartup>>,
        IClassFixture<HttpIntegrationTestFixture<ResourceIdLengthTests.LongIdStartup>>
    {
        private readonly HttpIntegrationTestFixture<StandardIdStartup> _standardFixture;
        private readonly HttpIntegrationTestFixture<LongIdStartup> _longFixture;

        public ResourceIdLengthTests(
            HttpIntegrationTestFixture<StandardIdStartup> standardFixture,
            HttpIntegrationTestFixture<LongIdStartup> longFixture)
        {
            _standardFixture = standardFixture;
            _longFixture = longFixture;
        }

        public static IEnumerable<object[]> Cases => ResourceIdBoundary.Cases.Select(c => new object[] { c.Profile == ResourceIdProfile.Long128, c.Length, c.Accepted });

        [Theory]
        [MemberData(nameof(Cases))]
        public async Task GivenTheLongResourceIdsFlag_WhenUsingTheHttpApi_ThenTheSelectedLimitIsApplied(bool useLongResourceIds, int length, bool valid)
        {
            Assert.SkipWhen(
                !_standardFixture.IsUsingInProcTestServer || !_longFixture.IsUsingInProcTestServer,
                "Requires in-process servers with distinct resource id configurations.");
            await ResourceIdBoundary.AssertAsync(useLongResourceIds ? _longFixture.HttpClient : _standardFixture.HttpClient, length, valid);
        }

        [RequiresIsolatedDatabase]
        public class StandardIdStartup : StartupBaseForCustomProviders
        {
            public StandardIdStartup(IConfiguration configuration)
                : base(new ConfigurationBuilder()
                    .AddConfiguration(configuration)
                    .AddInMemoryCollection(new Dictionary<string, string> { ["FhirServer:CoreFeatures:UseLongResourceIds"] = "false" })
                    .Build())
            {
            }
        }

        [RequiresIsolatedDatabase]
        public class LongIdStartup : StartupBaseForCustomProviders
        {
            public LongIdStartup(IConfiguration configuration)
                : base(new ConfigurationBuilder()
                    .AddConfiguration(configuration)
                    .AddInMemoryCollection(new Dictionary<string, string> { ["FhirServer:CoreFeatures:UseLongResourceIds"] = "true" })
                    .Build())
            {
            }
        }
    }
}
