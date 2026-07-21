// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Specification.Generated;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Ignixa;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Operations.Import
{
    [Trait(Traits.OwningTeam, OwningTeam.FhirImport)]
    [Trait(Traits.Category, Categories.Import)]
    public class IgnixaSchemaContextTests
    {
        [Fact]
        public void GivenAModelInfoProvider_WhenSchemaContextIsCreated_ThenGeneratedSchemaForCurrentFhirVersionIsSelected()
        {
            var context = new IgnixaSchemaContext(new VersionSpecificModelInfoProvider());

#if Stu3
            Assert.IsType<STU3CoreSchemaProvider>(context.Schema);
#elif R4B
            Assert.IsType<R4BCoreSchemaProvider>(context.Schema);
#elif R4
            Assert.IsType<R4CoreSchemaProvider>(context.Schema);
#elif R5
            Assert.IsType<R5CoreSchemaProvider>(context.Schema);
#else
#error Unsupported FHIR version
#endif
        }
    }
}
