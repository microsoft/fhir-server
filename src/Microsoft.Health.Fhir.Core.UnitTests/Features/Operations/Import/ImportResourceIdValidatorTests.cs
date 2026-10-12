// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Import
{
    [Trait(Traits.OwningTeam, OwningTeam.FhirImport)]
    [Trait(Traits.Category, Categories.Import)]
    public class ImportResourceIdValidatorTests
    {
        [Theory]
        [InlineData("abc", false)]
        [InlineData("abc", true)]
        [InlineData("A1-b.c", false)]
        [InlineData("A1-b.c", true)]
        [InlineData("0123456789012345678901234567890123456789012345678901234567890123", false)] // 64 chars
        [InlineData("0123456789012345678901234567890123456789012345678901234567890123", true)] // 64 chars
        [InlineData("01234567890123456789012345678901234567890123456789012345678901234", true)] // 65 chars
        [InlineData("01234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567", true)] // 128 chars
        public void GivenAValidResourceId_WhenValidated_ThenNoExceptionIsThrown(string resourceId, bool useLongResourceIds)
        {
            ImportResourceIdValidator.Validate(resourceId, ResourceIdPolicy.From(useLongResourceIds));
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData(null, true)]
        [InlineData("", false)]
        [InlineData("", true)]
        [InlineData("a/b", false)]
        [InlineData("a/b", true)]
        [InlineData("a_b", false)]
        [InlineData("a_b", true)]
        [InlineData("a b", false)]
        [InlineData("a b", true)]
        [InlineData("01234567890123456789012345678901234567890123456789012345678901234", false)] // 65 chars
        [InlineData("01234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567", false)] // 128 chars
        [InlineData("012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678", true)] // 129 chars
        public void GivenAnInvalidResourceId_WhenValidated_ThenBadRequestExceptionIsThrown(string resourceId, bool useLongResourceIds)
        {
            // Arrange
            var policy = ResourceIdPolicy.From(useLongResourceIds);

            // Act
            var exception = Record.Exception(() => ImportResourceIdValidator.Validate(resourceId, policy));

            // Assert
            var badRequest = Assert.IsType<BadRequestException>(exception);
            Assert.Contains(useLongResourceIds ? "128" : "64", badRequest.Message, System.StringComparison.Ordinal);
        }
    }
}
