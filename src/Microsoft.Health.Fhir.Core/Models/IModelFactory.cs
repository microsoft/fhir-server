// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.ElementModel;
using Hl7.FhirPath;

namespace Microsoft.Health.Fhir.Core.Models
{
    public interface IModelFactory
    {
        FhirSpecification Version { get; }

        string GetFhirTypeNameForType(Type type);

        bool IsKnownResource(string name);

        string[] GetResourceTypeNames();

        Type GetTypeForFhirType(string resourceType);

        EvaluationContext GetEvaluationContext(ITypedElement element);
    }
}
