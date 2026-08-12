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
        [InlineData("01234567890123456789012345678901234567890123456789012345678901234")] // 65 chars
        public void GivenAnInvalidResourceId_WhenValidated_ThenBadRequestExceptionIsThrown(string resourceId)
        {
            Assert.Throws<BadRequestException>(() => ImportResourceIdValidator.Validate(resourceId));
        }
    }
}
