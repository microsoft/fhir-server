// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.SqlServer.Registration;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Registration
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class FhirSqlServerConfigurationTests
    {
        [Fact]
        public void GivenNewConfiguration_WhenCreated_ThenScalarTemporalEqualityRewriterIsEnabledByDefault()
        {
            var configuration = new FhirSqlServerConfiguration();

            Assert.True(configuration.EnableScalarTemporalEqualityRewriter);
        }
    }
}
