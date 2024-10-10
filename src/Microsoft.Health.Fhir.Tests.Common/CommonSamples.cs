// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Specification;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Tests.Common
{
    public class CommonSamples
    {
        /// <summary>
        /// Loads a sample Resource
        /// </summary>
        public static ResourceElement GetJsonSample(string fileName, IModelInfoProvider modelInfoProvider = null)
        {
            EnsureArg.IsNotNullOrWhiteSpace(fileName, nameof(fileName));

            if (modelInfoProvider == null)
            {
                modelInfoProvider = MockModelInfoProviderBuilder
                    .Create(FhirSpecification.R4)
                    .Build();
            }

            return GetJsonSample(fileName, modelInfoProvider.Version, node => modelInfoProvider.ToTypedElement(node));
        }

        public static ResourceElement GetJsonSample(string fileName, FhirSpecification fhirSpecification, Func<ISourceNode, ITypedElement> convert)
        {
            EnsureArg.IsNotNullOrWhiteSpace(fileName, nameof(fileName));

            var fhirSource = EmbeddedResourceManager.GetStringContent("TestFiles", fileName, "json", fhirSpecification);

            var node = FhirJsonNode.Parse(fhirSource);

            var instance = convert(node);

            return new ResourceElement(instance);
        }
    }
}
