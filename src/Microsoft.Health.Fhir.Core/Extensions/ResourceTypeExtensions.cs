// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.ElementModel;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class ResourceTypeExtensions
    {
        public static Type ToResourceModelType(this ResourceElement resourceType)
        {
            return ModelInfoProvider.GetTypeForFhirType(resourceType.InstanceType);
        }

        public static ResourceElement ToResourceElement(this ITypedElement typedElement)
        {
            return new ResourceElement(typedElement);
        }
    }
}
