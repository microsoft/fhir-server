// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Filters.Metrics;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Controllers
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public sealed class FhirControllerTests
    {
        private readonly Type _targetFhirControllerClass = typeof(FhirController);

        [Fact]
        public void WhenProvidedAFhirController_CheckIfAllExpectedServiceFilterAttributesArePresent()
        {
            Type[] expectedCustomAttributes = new Type[]
            {
                typeof(AuditLoggingFilterAttribute),
                typeof(OperationOutcomeExceptionFilterAttribute),
                typeof(ValidateFormatParametersAttribute),
                typeof(QueryLatencyOverEfficiencyFilterAttribute),
            };

            ServiceFilterAttribute[] serviceFilterAttributes = Attribute.GetCustomAttributes(_targetFhirControllerClass, typeof(ServiceFilterAttribute))
                .Select(a => a as ServiceFilterAttribute)
                .ToArray();

            foreach (Type expectedCustomAttribute in expectedCustomAttributes)
            {
                bool attributeWasFound = serviceFilterAttributes.Any(s => s.ServiceType == expectedCustomAttribute);

                if (!attributeWasFound)
                {
                    string errorMessage = $"The custom attribute '{expectedCustomAttribute}' is not assigned to '{nameof(FhirController)}'.";
                    Assert.Fail(errorMessage);
                }
            }
        }

        [Fact]
        public void WhenProvidedAFhirController_CheckIfTheBundleEndpointHasTheLatencyMetricFilter()
        {
            Type expectedCustomAttribute = typeof(BundleEndpointMetricEmitterAttribute);

            TestIfTargetMethodContainsCustomAttribute(expectedCustomAttribute, "BatchAndTransactions", _targetFhirControllerClass);
        }

        [Fact]
        public void WhenProvidedAFhirController_CheckIfTheSearchEndpointsHaveTheLatencyMetricFilter()
        {
            Type expectedCustomAttribute = typeof(SearchEndpointMetricEmitterAttribute);

            TestIfTargetMethodContainsCustomAttribute(expectedCustomAttribute, "History", _targetFhirControllerClass);
            TestIfTargetMethodContainsCustomAttribute(expectedCustomAttribute, "Search", _targetFhirControllerClass);
            TestIfTargetMethodContainsCustomAttribute(expectedCustomAttribute, "SearchByResourceType", _targetFhirControllerClass);
            TestIfTargetMethodContainsCustomAttribute(expectedCustomAttribute, "SearchCompartmentByResourceType", _targetFhirControllerClass);
            TestIfTargetMethodContainsCustomAttribute(expectedCustomAttribute, "SystemHistory", _targetFhirControllerClass);
            TestIfTargetMethodContainsCustomAttribute(expectedCustomAttribute, "TypeHistory", _targetFhirControllerClass);
        }

        [Fact]
        public void WhenProvidedAFhirController_CheckIfTheCrudEndpointsHaveTheLatencyMetricFilter()
        {
            Type expectedCustomAttribute = typeof(CrudEndpointMetricEmitterAttribute);

            TestIfTargetMethodContainsCustomAttribute(expectedCustomAttribute, "Create", _targetFhirControllerClass);
            TestIfTargetMethodContainsCustomAttribute(expectedCustomAttribute, "Delete", _targetFhirControllerClass);
            TestIfTargetMethodContainsCustomAttribute(expectedCustomAttribute, "Read", _targetFhirControllerClass);
            TestIfTargetMethodContainsCustomAttribute(expectedCustomAttribute, "Update", _targetFhirControllerClass);
            TestIfTargetMethodContainsCustomAttribute(expectedCustomAttribute, "VRead", _targetFhirControllerClass);
        }

        private static void TestIfTargetMethodContainsCustomAttribute(Type expectedCustomAttributeType, string methodName, Type targetClassType)
        {
            MethodInfo bundleMethodInfo = targetClassType.GetMethod(methodName);
            Assert.True(bundleMethodInfo != null, $"The method '{methodName}' was not found in '{targetClassType.Name}'. Was it renamed or removed?");

            TypeFilterAttribute latencyFilter = Attribute.GetCustomAttributes(bundleMethodInfo, typeof(TypeFilterAttribute))
                .Select(a => a as TypeFilterAttribute)
                .Where(a => a.ImplementationType == expectedCustomAttributeType)
                .SingleOrDefault();

            Assert.True(latencyFilter != null, $"The expected filter '{expectedCustomAttributeType.Name}' was not found in the method '{methodName}' from '{targetClassType.Name}'.");
        }
    }
}
