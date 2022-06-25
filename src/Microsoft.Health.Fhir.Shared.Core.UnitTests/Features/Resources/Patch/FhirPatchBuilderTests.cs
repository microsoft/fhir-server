// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Resources.Patch.FhirPathPatch;
using Microsoft.Health.Fhir.Core.Features.Resources.Patch.FhirPathPatch.Helpers;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Resources.Patch
{
    public class FhirPatchBuilderTests
    {
        [Fact]
        public void GivenAFhirPatchParameterComponent_WhenOperationInvalid_ThenInvalidOperationExceptionIsThrown()
        {
            var patchParam = new Parameters().AddPatchParameter("remove", path: "Patient.identifier");

            var exception = Assert.Throws<InvalidOperationException>(() => new FhirPathPatchBuilder(new Patient(), patchParam));
            Assert.Equal("Invalid patch operation type: 'remove'. Only 'add', 'insert', 'delete', 'replace', and 'move' are allowed.", exception.Message);
        }

        [Fact]
        public void GivenAFhirPatchParameterComponent_WhenMissingPath_ThenInvalidOperationExceptionIsThrown()
        {
            var patchParam = new Parameters().AddPatchParameter("add", name: "identifier", value: new FhirString("test"));

            var exception = Assert.Throws<InvalidOperationException>(() => new FhirPathPatchBuilder(new Patient(), patchParam));
            Assert.Equal("Patch add operations must have the 'path' part.", exception.Message);
        }

        [Fact]
        public void GivenAFhirPatchParameterComponent_WhenMissingName_ThenInvalidOperationExceptionIsThrown()
        {
            var patchParam = new Parameters().AddPatchParameter("add", path: "Patient", value: new FhirString("test"));

            var exception = Assert.Throws<InvalidOperationException>(() => new FhirPathPatchBuilder(new Patient(), patchParam));
            Assert.Equal("Patch add operations must have the 'name' part.", exception.Message);
        }

        [Fact]
        public void GivenAFhirPatchParameterComponent_WhenMissingValue_ThenInvalidOperationExceptionIsThrown()
        {
            var patchParam = new Parameters().AddPatchParameter("add", path: "Patient", name: "identifier");

            var exception = Assert.Throws<InvalidOperationException>(() => new FhirPathPatchBuilder(new Patient(), patchParam));
            Assert.Equal("Patch add operations must have the 'value' part.", exception.Message);
        }
    }
}
