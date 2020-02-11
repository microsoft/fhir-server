// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.ElementModel;
using Hl7.FhirPath;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Conformance
{
    /// <summary>
    /// FHIR Stu3 specific conformance tests.
    /// </summary>
    public partial class ConformanceBuilderTests
    {
        [Fact]
        public void GivenAConformanceBuilder_WhenAddingDefaultInteractions_ThenProfileIsAddedAtResource()
        {
            _builder.AddDefaultResourceInteractions();

            ITypedElement statement = _builder.Build();

            object profile = statement.Scalar($"{ResourceQuery("Account")}.profile.reference");

            Assert.Equal("http://hl7.org/fhir/StructureDefinition/Account", profile);
        }
    }
}
