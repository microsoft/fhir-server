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
    public class CompartmentDefinitionManager : IStartable
    {
        private readonly FhirJsonParser _fhirJsonParser;
        private CompartmentDefinitionBuilder _compartmentDefinitionBuilder;

        public CompartmentDefinitionManager(FhirJsonParser fhirJsonParser)
        {
            EnsureArg.IsNotNull(fhirJsonParser, nameof(fhirJsonParser));
            _fhirJsonParser = fhirJsonParser;
        }

        public static Dictionary<ResourceType, CompartmentType> ResourceTypeToCompartmentType { get; } = new Dictionary<ResourceType, CompartmentType>
        {
            { ResourceType.Patient, CompartmentType.Patient },
            { ResourceType.Encounter, CompartmentType.Encounter },
            { ResourceType.Practitioner, CompartmentType.Practitioner },
            { ResourceType.RelatedPerson, CompartmentType.Practitioner },
            { ResourceType.Device, CompartmentType.Device },
        };

        public static Dictionary<CompartmentType, ResourceType> CompartmentTypeToResourceType { get; } = new Dictionary<CompartmentType, ResourceType>
        {
            { CompartmentType.Patient, ResourceType.Patient },
            { CompartmentType.Encounter, ResourceType.Encounter },
            { CompartmentType.Practitioner, ResourceType.Practitioner },
            { CompartmentType.RelatedPerson, ResourceType.Practitioner },
            { CompartmentType.Device, ResourceType.Device },
        };

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
                _compartmentDefinitionBuilder = new CompartmentDefinitionBuilder(bundle);
            }
        }

        /// <summary>
        /// Get the compartment index for the given <paramref name="resourceType"/>.
        /// </summary>
        /// <param name="resourceType">The fhir resource type for which to get the index.</param>
        /// <returns>The index of compartment type to fieldnames that represent the <paramref name="resourceType"/> in a compartment.</returns>
        public Dictionary<CompartmentType, HashSet<string>> GetCompartmentSearchParams(ResourceType resourceType)
        {
            if (_compartmentDefinitionBuilder.CompartmentSearchParams.TryGetValue(resourceType, out var compartmentSearchParams))
            {
                return compartmentSearchParams;
            }

            throw new ResourceNotSupportedException(resourceType.ToString());
        }

        /// <summary>
        /// Get the compartment index for the given <paramref name="resourceType"/>.
        /// </summary>
        /// <param name="resourceType">The fhir resource type for which to get the index.</param>
        /// <param name="compartmentSearchParams">On return, the index of compartment type to the fieldnames that represent the <paramref name="resourceType"/> in a compartment if it exists. Otherwise the default value.</param>
        /// <returns><c>true</c> if the compartmentSearchParams exists; otherwise, <c>false</c>.</returns>
        public bool TryGetCompartmentSearchParams(ResourceType resourceType, out Dictionary<CompartmentType, HashSet<string>> compartmentSearchParams)
        {
            return _compartmentDefinitionBuilder.CompartmentSearchParams.TryGetValue(resourceType, out compartmentSearchParams);
        }
    }
}
