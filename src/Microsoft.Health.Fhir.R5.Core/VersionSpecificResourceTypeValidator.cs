// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core
{
    /// <summary>
    /// Uses ResourceType enum for Stu3, R4 and R4B, but AllResourceTypes enum for R5
    /// </summary>
    public static class VersionSpecificResourceTypeValidator
    {
        public static bool IsValidResourceType(string resourceType)
        {
            return Enum.TryParse(resourceType, out AllResourceTypes result);
        }
    }
}
