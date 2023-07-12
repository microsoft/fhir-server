// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Validation;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Operations)]
public class ProfileValidatorTests
{
    [Fact]
    public void GivenAProfileValidator_WhenUsingReflectedVariables_TheyCanAllBeResolved()
    {
        (string Server, string CorePackageName, string ExpansionsPackageName) variables = ProfileValidator.GetFhirPackageVariables();

        Assert.NotEmpty(variables.Server);
        Assert.NotEmpty(variables.CorePackageName);
        Assert.NotEmpty(variables.ExpansionsPackageName);
    }
}
