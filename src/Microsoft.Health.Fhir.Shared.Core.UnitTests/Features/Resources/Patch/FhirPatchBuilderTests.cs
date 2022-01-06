// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using FhirPathPatch;
using FhirPathPatch.Helpers;
using Hl7.Fhir.Model;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Resources.Patch
{
    public class FhirPatchBuilderTests
    {
        [Fact]
        public void GivenAFhirPatchParameterComponent_WhenOperationInvalid_ThenInvalidOperationExceptionIsThrown()
        {
            var patchParam = new Parameters().AddPatchParameter("remove", path: "Patient.identifier");

            Assert.Throws<InvalidOperationException>(() => new FhirPathPatchBuilder(new Patient(), patchParam));
        }

        [Fact]
        public void GivenAFhirPatchParameterComponent_WhenMissingPath_ThenInvalidOperationExceptionIsThrown()
        {
            var patchParam = new Parameters().AddPatchParameter("add", name: "identifier", value: new FhirString("test"));

            Assert.Throws<InvalidOperationException>(() => new FhirPathPatchBuilder(new Patient(), patchParam));
        }

        [Fact]
        public void GivenAFhirPatchParameterComponent_WhenMissingName_ThenInvalidOperationExceptionIsThrown()
        {
            var patchParam = new Parameters().AddPatchParameter("add", path: "Patient", value: new FhirString("test"));

            Assert.Throws<InvalidOperationException>(() => new FhirPathPatchBuilder(new Patient(), patchParam));
        }

        [Fact]
        public void GivenAFhirPatchParameterComponent_WhenMissingValue_ThenInvalidOperationExceptionIsThrown()
        {
            var patchParam = new Parameters().AddPatchParameter("add", path: "Patient", name: "identifier");

            Assert.Throws<InvalidOperationException>(() => new FhirPathPatchBuilder(new Patient(), patchParam));
        }
    }
}
