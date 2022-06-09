// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Definition
{
    public static class ComponentDefinitionExtensions
    {
        public static Uri GetComponentDefinitionUri(this SearchParameter.ComponentComponent component)
        {
            return !string.IsNullOrEmpty(component.Definition) ? new Uri(component.Definition) : null;
        }

        public static string GetComponentDefinition(this ResourceReference component)
        {
            return component.Url?.ToString();
        }
    }
}
