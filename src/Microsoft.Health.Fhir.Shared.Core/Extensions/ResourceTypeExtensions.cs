// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class ResourceTypeExtensions
    {
        public static ResourceElement ToResourceElement(this Resource resource)
        {
            return new ResourceElement(resource.ToTypedElement(), resource);
        }

        public static bool EqualsString(this ResourceType resourceType, string value, bool ignoreCase = true)
        {
            return string.Equals(resourceType.ToString(), value, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
    }
}
