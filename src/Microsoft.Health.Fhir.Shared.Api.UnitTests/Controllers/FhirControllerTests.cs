// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Controllers
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public sealed class FhirControllerTests
    {
        [Fact]
        public void WhenProviderAFhirController_CheckIfAllExpectedServiceFilterAttributesArePresent()
        {
            Type[] expectedCustomAttributes = new Type[]
            {
                typeof(AuditLoggingFilterAttribute),
                typeof(OperationOutcomeExceptionFilterAttribute),
                typeof(ValidateFormatParametersAttribute),
                typeof(QueryLatencyOverEfficiencyFilterAttribute),
            };

            ServiceFilterAttribute[] serviceFilterAttributes = Attribute.GetCustomAttributes(typeof(FhirController), typeof(ServiceFilterAttribute))
                .Select(a => a as ServiceFilterAttribute)
                .ToArray();

            foreach (Type expectedCustomAttribute in expectedCustomAttributes)
            {
                bool attributeWasFound = serviceFilterAttributes.Any(s => s.ServiceType == expectedCustomAttribute);

                if (!attributeWasFound)
                {
                    string errorMessage = $"The custom attribute '{expectedCustomAttribute.ToString()}' is not assigned to '{nameof(FhirController)}'.";
                    Assert.Fail(errorMessage);
                }
            }
        }
    }
}
