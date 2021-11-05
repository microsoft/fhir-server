// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class ResourceElementExtensions
    {
        public static Type ToResourceModelType(this ResourceElement resourceType)
        {
            EnsureArg.IsNotNull(resourceType, nameof(resourceType));

            return ModelInfoProvider.GetTypeForFhirType(resourceType.InstanceType);
        }

        public static ResourceElement ToResourceElement(this ITypedElement typedElement)
        {
            return new ResourceElement(typedElement);
        }

        public static ResourceElement ToResourceElement(this Resource resource)
        {
            return new ResourceElement(resource.ToTypedElement(), resource);
        }
    }
}
