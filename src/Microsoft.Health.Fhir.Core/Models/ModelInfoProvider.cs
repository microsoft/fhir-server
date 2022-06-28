// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Specification;
using Hl7.FhirPath;

namespace Microsoft.Health.Fhir.Core.Models
{
    public static class ModelInfoProvider
    {
        private static IModelInfoProvider _modelInfoProvider;

        public static IModelInfoProvider Instance
        {
            get
            {
                EnsureProvider();
                return _modelInfoProvider;
            }
        }

        public static FhirSpecification Version
        {
            get
            {
                return Instance.Version;
            }
        }

        public static IStructureDefinitionSummaryProvider StructureDefinitionSummaryProvider
        {
            get
            {
                return Instance.StructureDefinitionSummaryProvider;
            }
        }

        public static void SetProvider(IModelInfoProvider modelInfoProvider)
        {
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));

            _modelInfoProvider = modelInfoProvider;
        }

        private static void EnsureProvider()
        {
            if (_modelInfoProvider == null)
            {
                throw new InvalidOperationException("Please call SetProvider before using methods on this class.");
            }
        }

        public static string GetFhirTypeNameForType(Type type)
        {
            return Instance.GetFhirTypeNameForType(type);
        }

        public static bool IsKnownResource(string name)
        {
            return Instance.IsKnownResource(name);
        }

        public static bool IsKnownCompartmentType(string compartmentType)
        {
            return Instance.IsKnownCompartmentType(compartmentType);
        }

        public static IReadOnlyCollection<string> GetResourceTypeNames()
        {
            return Instance.GetResourceTypeNames();
        }

        public static IReadOnlyCollection<string> GetCompartmentTypeNames()
        {
            return Instance.GetCompartmentTypeNames();
        }

        public static Type GetTypeForFhirType(string resourceType)
        {
            return Instance.GetTypeForFhirType(resourceType);
        }

        public static EvaluationContext GetEvaluationContext(Func<string, ITypedElement> elementResolver = null)
        {
            return Instance.GetEvaluationContext(elementResolver);
        }

        public static void EnsureValidResourceType(string resourceName, string parameterName)
        {
            if (!string.IsNullOrEmpty(resourceName) && !IsKnownResource(resourceName))
            {
                throw new ArgumentException(string.Format(Resources.ResourceNotSupported, resourceName), parameterName);
            }
        }
    }
}
