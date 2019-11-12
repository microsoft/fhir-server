// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Api.Features.Resources.Bundle;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Resources.Bundle
{
    public class TransactionValidatorTests
    {
        [Fact]
        public void GivenABundleWithUniqueResources_BundleValidatorShouldNotThrowException()
        {
            ValidateIfBundleEntryIsUnique(Samples.GetDefaultTransaction());
        }

        [Fact]
        public void GivenATransactionBundle_IfContainsMultipleResourcesWithSameFullUrl_BundleValidatorShouldThrowException()
        {
            var requestBundle = Samples.GetJsonSample("Bundle-TransactionWithMultipleResourcesWithSameFullUrl");

            ValidateIfBundleEntryIsUnique(requestBundle);
        }

        [Fact]
        public void GivenATransactionBundle_IfContainsMultipleEntriesModifyingSameResource_BundleValidatorShouldThrowException()
        {
            var requestBundle = Samples.GetJsonSample("Bundle-TransactionWithMultipleEntriesModifyingSameResource");

            ValidateIfBundleEntryIsUnique(requestBundle);
        }

        private static void ValidateIfBundleEntryIsUnique(Core.Models.ResourceElement requestBundle)
        {
            try
            {
                var resourceUrlList = new HashSet<string>();
                foreach (Hl7.Fhir.Model.Bundle.EntryComponent entry in requestBundle.ToPoco<Hl7.Fhir.Model.Bundle>().Entry)
                {
                    TransactionValidator.ValidateTransaction(resourceUrlList, entry);
                }
            }
            catch (Exception ex)
            {
                Assert.True(ex is RequestNotValidException);
            }
        }
    }
}
