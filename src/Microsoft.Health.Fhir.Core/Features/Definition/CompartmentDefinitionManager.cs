// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Definition
{
    /// <summary>
    /// Manager to access compartment definitions.
    /// </summary>
    public class CompartmentDefinitionManager : IStartable, ICompartmentDefinitionManager
    {
        private readonly FhirJsonParser _fhirJsonParser;
        private IDictionary<CompartmentType, CompartmentDefinition> _compartmentlLookup;
        private IDictionary<ResourceType, IDictionary<CompartmentType, IList<string>>> _compartmentSearchParams;

        public CompartmentDefinitionManager(FhirJsonParser fhirJsonParser)
        {
            EnsureArg.IsNotNull(fhirJsonParser, nameof(fhirJsonParser));
            _fhirJsonParser = fhirJsonParser;
        }

        public void Start()
        {
            Type type = GetType();

            // The json file is a bundle compiled from the compartment definitions currently defined by HL7.
            // The definitions are available at https://www.hl7.org/fhir/compartmentdefinition.html.
            using (Stream stream = type.Assembly.GetManifestResourceStream($"{type.Namespace}.compartment.json"))
            using (TextReader reader = new StreamReader(stream))
            using (JsonReader jsonReader = new JsonTextReader(reader))
            {
                var bundle = _fhirJsonParser.Parse<Bundle>(jsonReader);

                var builder = new CompartmentDefinitionBuilder(bundle);

                builder.Build();
                _compartmentSearchParams = builder.CompartmentSearchParams;
                _compartmentlLookup = builder.CompartmentLookup;
            }
        }

        public CompartmentDefinition GetCompartmentDefinition(CompartmentType compartmentType)
        {
            if (_compartmentlLookup.TryGetValue(compartmentType, out var compartmentDefinition))
            {
                return compartmentDefinition;
            }

            throw new NotSupportedException(compartmentType.ToString());
        }

        public IDictionary<CompartmentType, IList<string>> GetCompartmentSearchParams(ResourceType resourceType)
        {
            if (_compartmentSearchParams.TryGetValue(resourceType, out var compartmentSearchParams))
            {
                return compartmentSearchParams;
            }

            throw new ResourceNotSupportedException(resourceType.ToString());
        }

        public bool TryGetCompartmentSearchParams(ResourceType resourceType, out IDictionary<CompartmentType, IList<string>> compartmentSearchParams)
        {
            return _compartmentSearchParams.TryGetValue(resourceType, out compartmentSearchParams);
        }
    }
}
