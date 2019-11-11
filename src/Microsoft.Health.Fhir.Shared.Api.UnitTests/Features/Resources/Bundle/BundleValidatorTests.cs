// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Api.Features.Resources.Bundle;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Resources.Bundle
{
    public class BundleValidatorTests
    {
        [Fact]
        public void GivenABundleWithUniqueResources_BundleValidatorShouldReturnTrue()
        {
            var requestBundle = Samples.GetDefaultTransaction();

            BundleValidator.ValidateTransactionBundle(requestBundle.ToPoco<Hl7.Fhir.Model.Bundle>());
        }

        [Fact]
        public void GivenABundleWithDupliateResourceIds_BundleValidatorShouldReturnFalse()
        {
            var requestBundle = Samples.GetJsonSample("Bundle-TransactionWithDuplicateResources");

            Assert.Throws<RequestNotValidException>(() => BundleValidator.ValidateTransactionBundle(requestBundle.ToPoco<Hl7.Fhir.Model.Bundle>()));
        }
    }
}
