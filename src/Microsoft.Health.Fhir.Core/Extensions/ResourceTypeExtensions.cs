// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class ResourceTypeExtensions
    {
        public static Type ToResourceModelType(this ResourceType resourceType)
        {
            return ModelInfo.GetTypeForFhirType(resourceType.ToString());
        }
    }
}
