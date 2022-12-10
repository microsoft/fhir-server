// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Stu3.Core.UnitTests.Features.Operations.Versions
{
    /// <summary>
    /// Provides STU3 specific tests.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class VersionSpecificTests
    {
        private readonly IModelInfoProvider _provider;

        public VersionSpecificTests()
        {
            _provider = new VersionSpecificModelInfoProvider();
        }

        [Fact]
        public void GivenStu3Server_WhenSupportedVersionIsRequested_ThenCorrectVersionShouldBeReturned()
        {
            var version = _provider.SupportedVersion.ToString();

            Assert.Equal("3.0.2", version);
        }
    }
}
