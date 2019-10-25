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
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    internal class CapabilityStatementBuilder : ICapabilityStatementBuilder
    {
        private readonly ListedCapabilityStatement _statement;
        private readonly IModelInfoProvider _modelInfoProvider;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;

        private CapabilityStatementBuilder(ListedCapabilityStatement statement, IModelInfoProvider modelInfoProvider, ISearchParameterDefinitionManager searchParameterDefinitionManager)
        {
            EnsureArg.IsNotNull(statement, nameof(statement));
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));

            _statement = statement;
            _modelInfoProvider = modelInfoProvider;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
        }

        public static ICapabilityStatementBuilder Create(IModelInfoProvider modelInfoProvider, ISearchParameterDefinitionManager searchParameterDefinitionManager)
        {
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));

            string manifestName = $"{typeof(CapabilityStatementBuilder).Namespace}.{modelInfoProvider.Version}.BaseCapabilities.json";

            using (Stream resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(manifestName))
            using (var reader = new StreamReader(resourceStream))
            {
                var statement = JsonConvert.DeserializeObject<ListedCapabilityStatement>(reader.ReadToEnd());

                return new CapabilityStatementBuilder(statement, modelInfoProvider, searchParameterDefinitionManager);
            }
        }

        public ICapabilityStatementBuilder Update(Action<ListedCapabilityStatement> action)
        {
            EnsureArg.IsNotNull(action, nameof(action));

            action(_statement);

            return this;
        }

        public ICapabilityStatementBuilder UpdateRestResourceComponent(string resourceType, Action<ListedResourceComponent> action)
        {
            EnsureArg.IsNotNullOrEmpty(resourceType, nameof(resourceType));
            EnsureArg.IsNotNull(action, nameof(action));
            EnsureArg.IsTrue(_modelInfoProvider.IsKnownResource(resourceType), nameof(resourceType), x => GenerateTypeErrorMessage(x, resourceType));

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
            EnsureArg.IsNotNullOrEmpty(resourceType, nameof(resourceType));
            EnsureArg.IsNotNullOrEmpty(interaction, nameof(interaction));
            EnsureArg.IsTrue(_modelInfoProvider.IsKnownResource(resourceType), nameof(resourceType), x => GenerateTypeErrorMessage(x, resourceType));

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
            EnsureArg.IsNotNull(searchParameters, nameof(searchParameters));
            EnsureArg.IsTrue(_modelInfoProvider.IsKnownResource(resourceType), nameof(resourceType), x => GenerateTypeErrorMessage(x, resourceType));

            UpdateRestResourceComponent(resourceType, c =>
            {
                foreach (SearchParamComponent searchParam in searchParameters)
                {
                    c.SearchParam.Add(searchParam);
                }
            });

            return this;
        }

        public ICapabilityStatementBuilder AddDefaultResourceInteractions()
        {
            foreach (var resource in _modelInfoProvider.GetResourceTypeNames())
            {
                if (string.Equals(resource, KnownResourceTypes.Parameters, StringComparison.Ordinal))
                {
                    continue;
                }

                TryAddRestInteraction(resource, TypeRestfulInteraction.Create);
                TryAddRestInteraction(resource, TypeRestfulInteraction.Read);

                if (!string.Equals(resource, KnownResourceTypes.AuditEvent, StringComparison.Ordinal))
                {
                    if (!string.Equals(resource, KnownResourceTypes.Binary, StringComparison.Ordinal))
                    {
                        TryAddRestInteraction(resource, TypeRestfulInteraction.Vread);
                    }

                    TryAddRestInteraction(resource, TypeRestfulInteraction.Update);
                    TryAddRestInteraction(resource, TypeRestfulInteraction.Delete);
                }

                UpdateRestResourceComponent(resource, component =>
                {
                    component.Versioning.Add(ResourceVersionPolicy.NoVersion);
                    component.Versioning.Add(ResourceVersionPolicy.Versioned);
                    component.Versioning.Add(ResourceVersionPolicy.VersionedUpdate);

                    component.ReadHistory = true;
                    component.UpdateCreate = true;
                });
            }

            return this;
        }

        public ICapabilityStatementBuilder AddDefaultSearchParameters()
        {
            foreach (var resource in _modelInfoProvider.GetResourceTypeNames())
            {
                if (string.Equals(resource, KnownResourceTypes.Parameters, StringComparison.Ordinal))
                {
                    continue;
                }

                IEnumerable<SearchParameterInfo> searchParams = _searchParameterDefinitionManager.GetSearchParameters(resource);

                if (searchParams.Any())
                {
                    TryAddSearchParams(resource, searchParams.Select(x => new SearchParamComponent
                    {
                        Name = x.Name,
                        Type = x.Type,
                        Definition = x.Url,
                        Documentation = x.Description,
                    }));

                    TryAddRestInteraction(resource, TypeRestfulInteraction.SearchType);
                }

                TryAddRestInteraction(resource, TypeRestfulInteraction.HistoryType);
                TryAddRestInteraction(resource, TypeRestfulInteraction.HistoryInstance);
            }

            TryAddRestInteraction(SystemRestfulInteraction.HistorySystem);

            return this;
        }

        public ITypedElement Build()
        {
            // To build a CapabilityStatement we use a custom JsonConverter that serializes
            // the ListedCapabilityStatement into a CapabilityStatement poco

            var json = JsonConvert.SerializeObject(_statement, new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Converters = new List<JsonConverter>
                {
                    new DefaultOptionHashSetJsonConverter(),
                    new EnumLiteralJsonConverter(),
                },
                NullValueHandling = NullValueHandling.Ignore,
            });

            ISourceNode jsonStatement = FhirJsonNode.Parse(json);

            // Using a version specific StructureDefinitionSummaryProvider ensures the metadata to be
            // compatible with the current FhirSerializer/output formatter.
            return jsonStatement.ToTypedElement(_modelInfoProvider.GetStructureDefinitionSummaryProvider());
        }

        private EnsureOptions GenerateTypeErrorMessage(EnsureOptions options, string resourceType)
        {
            return options.WithMessage($"Unknown resource type {resourceType}");
        }
    }
}
