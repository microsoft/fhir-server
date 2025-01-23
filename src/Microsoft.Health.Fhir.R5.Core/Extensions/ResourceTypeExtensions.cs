// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.R5.Core.Extensions
{
    public static class ResourceTypeExtensions
    {
        public static ResourceType? ToResourceType(this VersionIndependentResourceTypesAll? resourceType)
        {
            if (Enum.TryParse<ResourceType>(resourceType?.ToString(), out var value))
            {
                return value;
            }

            return null;
        }

        public static VersionIndependentResourceTypesAll? ToVersionIndependentResourceTypesAll(this ResourceType? resourceType)
        {
            if (Enum.TryParse<VersionIndependentResourceTypesAll>(resourceType?.ToString(), out var value))
            {
                return value;
            }

            return null;
        }
    }
}
