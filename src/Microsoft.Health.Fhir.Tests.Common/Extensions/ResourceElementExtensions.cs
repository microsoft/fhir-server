// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Tests.Common.Extensions
{
    public static class ResourceElementExtensions
    {
        public static bool SupportsSearchParameter(this ResourceElement element, string resource, string parameterName)
        {
            EnsureArg.IsNotNull(element, nameof(element));
            EnsureArg.IsNotNullOrEmpty(resource, nameof(resource));
            EnsureArg.IsNotNullOrEmpty(parameterName, nameof(parameterName));

            return element.Predicate($"CapabilityStatement.rest.resource.where(type = '{resource}').searchParam.where(name = '{parameterName}').exists()");
        }

        public static bool SupportsTerminologyOperation(this ResourceElement element, string operation)
        {
            EnsureArg.IsNotNull(element, nameof(element));

            return element.Predicate($"CapabilityStatement.rest.operation.where(name = '{operation}').exists()");
        }
    }
}
