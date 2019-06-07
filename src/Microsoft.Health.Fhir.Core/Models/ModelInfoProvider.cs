// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.FhirPath;

namespace Microsoft.Health.Fhir.Core.Models
{
    public static class ModelInfoProvider
    {
        private static IModelInfoProvider _modelInfoProvider;

        public static FhirSpecification Version
        {
            get
            {
                EnsureProvider();
                return _modelInfoProvider.Version;
            }
        }

        public static IModelInfoProvider Instance
        {
            get
            {
                EnsureProvider();
                return _modelInfoProvider;
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
                throw new Exception("Please call SetProvider before using methods on this class.");
            }
        }

        public static string GetFhirTypeNameForType(Type type)
        {
            EnsureProvider();
            return _modelInfoProvider.GetFhirTypeNameForType(type);
        }

        public static bool IsKnownResource(string name)
        {
            EnsureProvider();
            return _modelInfoProvider.IsKnownResource(name);
        }

        public static bool IsKnownCompartmentType(string compartmentType)
        {
            EnsureProvider();
            return _modelInfoProvider.IsKnownCompartmentType(compartmentType);
        }

        public static string[] GetResourceTypeNames()
        {
            EnsureProvider();
            return _modelInfoProvider.GetResourceTypeNames();
        }

        public static string[] GetCompartmentTypeNames()
        {
            EnsureProvider();
            return _modelInfoProvider.GetCompartmentTypeNames();
        }

        public static Type GetTypeForFhirType(string resourceType)
        {
            EnsureProvider();
            return _modelInfoProvider.GetTypeForFhirType(resourceType);
        }

        public static EvaluationContext GetEvaluationContext(ITypedElement element)
        {
            EnsureProvider();
            return _modelInfoProvider.GetEvaluationContext(element);
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
