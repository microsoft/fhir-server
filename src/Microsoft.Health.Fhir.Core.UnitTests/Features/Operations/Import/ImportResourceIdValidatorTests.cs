// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Persistence;
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
        [InlineData("abc")]
        [InlineData("A1-b.c")]
        [InlineData("0123456789012345678901234567890123456789012345678901234567890123")] // 64 chars
        public void GivenAValidResourceId_WhenValidated_ThenNoExceptionIsThrown(string resourceId)
        {
            ImportResourceIdValidator.Validate(resourceId);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("a/b")]
        [InlineData("abc\n")]
        [InlineData("abc\r\n")]
        [InlineData("a_b")]
        [InlineData("a b")]
        [InlineData("01234567890123456789012345678901234567890123456789012345678901234")] // 65 chars
        public void GivenAnInvalidResourceId_WhenValidated_ThenBadRequestExceptionIsThrown(string resourceId)
        {
            Assert.Throws<BadRequestException>(() => ImportResourceIdValidator.Validate(resourceId));
        }

        [Theory]
        [InlineData(64, false, true)]
        [InlineData(65, false, false)]
        [InlineData(128, false, false)]
        [InlineData(64, true, true)]
        [InlineData(65, true, true)]
        [InlineData(128, true, true)]
        [InlineData(129, true, false)]
        public void GivenAnIdAndTheLongResourceIdsFlag_WhenValidated_ThenTheSelectedLimitIsApplied(int idLength, bool useLongResourceIds, bool valid)
        {
            // Arrange
            string resourceId = new string('a', idLength);

            // Act
            var exception = Record.Exception(() => ImportResourceIdValidator.Validate(resourceId, useLongResourceIds));

            // Assert
            if (valid)
            {
                Assert.Null(exception);
            }
            else
            {
                var badRequest = Assert.IsType<BadRequestException>(exception);
                Assert.Contains(useLongResourceIds ? "128" : "64", badRequest.Message, System.StringComparison.Ordinal);
            }
        }

        [Theory]
        [InlineData("abc\n")]
        [InlineData("abc\r\n")]
        [InlineData("a_b")]
        [InlineData("a/b")]
        [InlineData("")]
        [InlineData(null)]
        public void GivenAnInvalidLongResourceId_WhenValidated_ThenBadRequestExceptionIsThrown(string resourceId)
        {
            // Arrange
            const bool useLongResourceIds = true;

            // Act
            var exception = Record.Exception(() => ImportResourceIdValidator.Validate(resourceId, useLongResourceIds));

            // Assert
            Assert.IsType<BadRequestException>(exception);
        }
    }
}
