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
    public static class ModelFactory
    {
        private static IModelFactory _modelFactory;

        public static void SetModelFactory(IModelFactory modelFactory)
        {
            EnsureArg.IsNotNull(modelFactory, nameof(modelFactory));

            _modelFactory = modelFactory;
        }

        private static void EnsureModelFactory()
        {
            if (_modelFactory == null)
            {
                throw new Exception("Please call SetModelFactory before using methods on this class.");
            }
        }

        public static string GetFhirTypeNameForType(Type type)
        {
            EnsureModelFactory();
            return _modelFactory.GetFhirTypeNameForType(type);
        }

        public static bool IsKnownResource(string name)
        {
            EnsureModelFactory();
            return _modelFactory.IsKnownResource(name);
        }

        public static string[] GetResourceTypeNames()
        {
            EnsureModelFactory();
            return _modelFactory.GetResourceTypeNames();
        }

        public static Type GetTypeForFhirType(string resourceType)
        {
            EnsureModelFactory();
            return _modelFactory.GetTypeForFhirType(resourceType);
        }

        public static EvaluationContext GetEvaluationContext(ITypedElement element)
        {
            EnsureModelFactory();
            return _modelFactory.GetEvaluationContext(element);
        }
    }
}
