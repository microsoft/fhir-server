// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.FhirPath;
using Hl7.Fhir.Model;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core
{
    /// <summary>
    /// Provides agnostic access to FHIR Models and Resources
    /// </summary>
    public partial class VersionSpecificModelInfoProvider : IModelInfoProvider
    {
        public string GetFhirTypeNameForType(Type type)
        {
            return ModelInfo.GetFhirTypeNameForType(type);
        }

        public bool IsKnownResource(string name)
        {
            return ModelInfo.IsKnownResource(name);
        }

        public bool IsKnownCompartmentType(string compartmentType)
        {
            return Enum.IsDefined(typeof(CompartmentType), compartmentType);
        }

        public string[] GetResourceTypeNames()
        {
            return Enum.GetNames(typeof(ResourceType));
        }

        public string[] GetCompartmentTypeNames()
        {
            return Enum.GetNames(typeof(CompartmentType));
        }

        public Type GetTypeForFhirType(string resourceType)
        {
            return ModelInfo.GetTypeForFhirType(resourceType);
        }

        public EvaluationContext GetEvaluationContext(ITypedElement element)
        {
            return new FhirEvaluationContext(element);
        }
    }
}
