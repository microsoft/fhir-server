// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Specification;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SourceNodeSerialization.SourceNodes.Models;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class ResourceTypeExtensions
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

        public static ResourceElement ToResourceElement(this ResourceJsonNode typedElement, IStructureDefinitionSummaryProvider structureDefinitionSummaryProvider)
        {
            return new ResourceElement(typedElement.ToSourceNode().ToTypedElement(structureDefinitionSummaryProvider), typedElement);
        }
    }
}
