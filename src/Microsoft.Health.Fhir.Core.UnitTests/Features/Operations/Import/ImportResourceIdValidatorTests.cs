// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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
            ImportResourceIdValidator.Validate(resourceId, 64);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("a/b")]
        [InlineData("01234567890123456789012345678901234567890123456789012345678901234")] // 65 chars
        public void GivenAnInvalidResourceId_WhenValidated_ThenBadRequestExceptionIsThrown(string resourceId)
        {
            Assert.Throws<BadRequestException>(() => ImportResourceIdValidator.Validate(resourceId, 64));
        }

        [Theory]
        [InlineData(65)]
        [InlineData(128)]
        public void GivenAnIdWithinAConfiguredMaxLength_WhenValidated_ThenNoExceptionIsThrown(int idLength)
        {
            // Arrange
            string resourceId = new string('a', idLength);

            // Act
            Exception exception = Record.Exception(() => ImportResourceIdValidator.Validate(resourceId, 128));

            // Assert
            Assert.Null(exception);
        }

        [Theory]
        [InlineData(65)]
        [InlineData(128)]
        public void GivenAnIdLongerThanTheDefaultMaxLength_WhenValidatedWithTheDefaultMaxLength_ThenBadRequestExceptionIsThrown(int idLength)
        {
            // Arrange
            string resourceId = new string('a', idLength);

            // Act & Assert
            Assert.Throws<BadRequestException>(() => ImportResourceIdValidator.Validate(resourceId, 64));
        }

        [Fact]
        public void GivenAnIdLongerThanTheConfiguredMaxLength_WhenValidated_ThenBadRequestExceptionIsThrown()
        {
            // Arrange
            string resourceId = new string('a', 129);

            // Act & Assert
            Assert.Throws<BadRequestException>(() => ImportResourceIdValidator.Validate(resourceId, 128));
        }

        [Theory]
        [InlineData(64)]
        [InlineData(128)]
        public void GivenAnIdWithATrailingNewline_WhenValidated_ThenTheExistingRegexBehaviorIsPreserved(int maxLength)
        {
            // Arrange
            string resourceId = new string('a', maxLength) + "\n";

            // Act
            Exception exception = Record.Exception(() => ImportResourceIdValidator.Validate(resourceId, maxLength));

            // Assert
            Assert.Null(exception);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("a/b")]
        public void GivenAnInvalidResourceId_WhenValidatedWithAConfiguredMaxLength_ThenBadRequestExceptionIsThrown(string resourceId)
        {
            // Act & Assert
            Assert.Throws<BadRequestException>(() => ImportResourceIdValidator.Validate(resourceId, 128));
        }
    }
}
