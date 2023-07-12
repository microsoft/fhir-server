// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core
{
    /// <summary>
    /// Uses ResourceType enum for Stu3, R4 and R4B, but for R5, Resource and DomainResouce not in the Enum
    /// so we check those separately
    /// </summary>
    public static class VersionSpecificResourceTypeValidator
    {
        public static bool IsValidResourceType(string resourceType)
        {
            if (Enum.TryParse(resourceType, out ResourceType result) == false)
            {
               return resourceType.Equals("Resource", StringComparison.Ordinal) || resourceType.Equals("DomainResource", StringComparison.Ordinal);
            }
            else
            {
                return true;
            }
        }
    }
}
