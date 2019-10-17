// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.Core.Features.Conformance.Serialization;
using Microsoft.Health.Fhir.Core.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    internal class CapabilityStatementBuilder : ICapabilityStatementBuilder
    {
        private readonly ListedCapabilityStatement _statement;

        private CapabilityStatementBuilder(ListedCapabilityStatement statement)
        {
            _statement = statement;
        }

        public static ICapabilityStatementBuilder Create(FhirSpecification fhirSpecification)
        {
            string manifestName = $"{typeof(CapabilityStatementBuilder).Namespace}.{fhirSpecification}.BaseCapabilities.json";

            using (Stream resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(manifestName))
            using (var reader = new StreamReader(resourceStream))
            {
                var statement = JsonConvert.DeserializeObject<ListedCapabilityStatement>(reader.ReadToEnd());

                return new CapabilityStatementBuilder(statement);
            }
        }

        public ICapabilityStatementBuilder Update(Action<ListedCapabilityStatement> action)
        {
            action(_statement);

            return this;
        }

        public ICapabilityStatementBuilder UpdateRestResourceComponent(string resourceType, Action<ListedResourceComponent> action)
        {
            EnsureArg.IsNotNullOrEmpty(resourceType, nameof(resourceType));

            ListedRestComponent listedRestComponent = _statement.Rest.Server();
            ListedResourceComponent resourceComponent = listedRestComponent.Resource.SingleOrDefault(x => string.Equals(x.Type, resourceType, StringComparison.OrdinalIgnoreCase));

            if (resourceComponent == null)
            {
                resourceComponent = new ListedResourceComponent
                {
                    Type = resourceType,
                };
                listedRestComponent.Resource.Add(resourceComponent);
            }

            action(resourceComponent);

            return this;
        }

        public ICapabilityStatementBuilder TryAddRestInteraction(string resourceType, string interaction)
        {
            EnsureArg.IsNotNullOrEmpty(interaction, nameof(interaction));

            UpdateRestResourceComponent(resourceType, c =>
            {
                c.Interaction.Add(new ResourceInteractionComponent
                {
                    Code = interaction,
                });
            });

            return this;
        }

        public ICapabilityStatementBuilder TryAddRestInteraction(string systemInteraction)
        {
            EnsureArg.IsNotNullOrEmpty(systemInteraction, nameof(systemInteraction));

            _statement.Rest.Server().Interaction.Add(new ResourceInteractionComponent { Code = systemInteraction });

            return this;
        }

        public ICapabilityStatementBuilder TryAddSearchParams(string resourceType, IEnumerable<SearchParamComponent> searchParameters)
        {
            EnsureArg.IsNotNullOrEmpty(resourceType, nameof(resourceType));

            UpdateRestResourceComponent(resourceType, c =>
            {
                foreach (SearchParamComponent searchParam in searchParameters)
                {
                    c.SearchParam.Add(searchParam);
                }
            });

            return this;
        }

        public ITypedElement Build()
        {
            // To build a CapabilityStatement we use a custom JsonConverter that serializes
            // the ListedCapabilityStatement into a CapabilityStatement poco

            var json = JsonConvert.SerializeObject(_statement, new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Converters = new List<JsonConverter> { new ListedCapabilityStatementConverter() },
            });

            ISourceNode jsonStatement = FhirJsonNode.Parse(json);

            // Using a version specific StructureDefinitionSummaryProvider ensures the metadata to be
            // compatible with the current FhirSerializer/output formatter.
            return jsonStatement.ToTypedElement(ModelInfoProvider.GetStructureDefinitionSummaryProvider());
        }
    }
}
