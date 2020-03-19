// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Specification;
using Hl7.FhirPath;

namespace Microsoft.Health.Fhir.Core.Models
{
    public interface IModelInfoProvider
    {
        FhirSpecification Version { get; }

        Version SupportedVersion { get; }

        IStructureDefinitionSummaryProvider StructureDefinitionSummaryProvider { get; }

        string GetFhirTypeNameForType(Type type);

        bool IsKnownResource(string name);

        bool IsKnownCompartmentType(string compartmentType);

        IReadOnlyCollection<string> GetResourceTypeNames();

        IReadOnlyCollection<string> GetCompartmentTypeNames();

        Type GetTypeForFhirType(string resourceType);

        EvaluationContext GetEvaluationContext(ITypedElement element);
    }
}
