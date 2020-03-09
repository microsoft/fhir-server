// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Introspection;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    public static class PropertyMappingExtensions
    {
        public static Type GetElementType(this PropertyMapping mapping)
        {
            return mapping.ElementType;
        }
    }
}
