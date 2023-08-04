// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Extensions;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Import)]
public class KnowFhirPathsTests
{
    [Fact]
    public void GivenAResource_WhenEvaluatingIfSoftDeleted_ThenTheCorrectFlagIsReturned()
    {
        ResourceElement patient = Samples.GetDefaultPatient();

        var isDeleted = patient.IsSoftDeleted();
        Assert.False(isDeleted);

        ResourceElement withExtension = patient.TryAddSoftDeletedExtension();

        isDeleted = withExtension.IsSoftDeleted();
        Assert.True(isDeleted);
    }
}
