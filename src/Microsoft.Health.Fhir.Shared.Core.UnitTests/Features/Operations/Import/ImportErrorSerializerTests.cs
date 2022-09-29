// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Shared.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Operations.Import
{
    [Trait("Traits.OwningTeam", OwningTeam.FhirImport)]
    [Trait(Traits.Category, Categories.Import)]
    public class ImportErrorSerializerTests
    {
        private readonly FhirJsonSerializer _jsonSerializer = new FhirJsonSerializer();

        [Fact]
        public void GivenImportProcessError_WhenSerialize_ValidStringShouldBeReturned()
        {
            string errorMessage = "Test Error";
            ImportErrorSerializer serializer = new ImportErrorSerializer(_jsonSerializer);

            string outcome = serializer.Serialize(10, new Exception(errorMessage));

            FhirJsonParser parser = new FhirJsonParser();
            OperationOutcome operationOutcome = parser.Parse<OperationOutcome>(outcome);

            Assert.Equal(OperationOutcome.IssueSeverity.Error, operationOutcome.Issue[0].Severity);
            Assert.Equal($"Failed to process resource at line: {10}", operationOutcome.Issue[0].Diagnostics);
            Assert.Equal(errorMessage, operationOutcome.Issue[0].Details.Text);
        }
    }
}
