// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, "ResourceIdBoundary")]
    [Trait("DataStore", nameof(DataStore.SqlServer))]
    public class RemoteResourceIdLengthTests : IClassFixture<RemoteResourceIdFixture>
    {
        private readonly RemoteResourceIdFixture _fixture;

        public RemoteResourceIdLengthTests(RemoteResourceIdFixture fixture)
        {
            _fixture = fixture;
        }

        public static IEnumerable<object[]> Cases
        {
            get
            {
                string configured = Environment.GetEnvironmentVariable("ResourceIdProfile");
                ResourceIdProfile profile = configured == null ? ResourceIdProfile.Standard64 : Enum.Parse<ResourceIdProfile>(configured);
                return ResourceIdBoundary.Cases.Where(c => c.Profile == profile).Select(c => new object[] { ResourceIdBoundary.CaseId(c) });
            }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public async Task GivenResourceIdBoundary_WhenUsingRemoteSql_ThenExpectedStatusCodesAreReturned(string caseId)
        {
            Assert.SkipWhen(Environment.GetEnvironmentVariable("ResourceIdProfile") == null, "Select a fixed remote ResourceIdProfile to run this contract.");
            var boundary = Assert.Single(ResourceIdBoundary.Cases, c => ResourceIdBoundary.CaseId(c) == caseId);
            await ResourceIdBoundary.AssertAsync(_fixture.HttpClient, boundary.Length, boundary.Accepted);
        }
    }
}
