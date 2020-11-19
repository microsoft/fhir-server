// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Data;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.Core.Features.Conformance.Serialization;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search;
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

            using Stream resourceStream = modelInfoProvider.OpenVersionedFileStream("BaseCapabilities.json");
            using var reader = new StreamReader(resourceStream);
            var statement = JsonConvert.DeserializeObject<ListedCapabilityStatement>(reader.ReadToEnd());

            return new CapabilityStatementBuilder(statement, modelInfoProvider, searchParameterDefinitionManager);
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
                    Profile = new ReferenceComponent
                    {
                        Reference = $"http://hl7.org/fhir/StructureDefinition/{resourceType}",
                    },
                };
                listedRestComponent.Resource.Add(resourceComponent);
            }

            action(resourceComponent);

            return this;
        }

        public ICapabilityStatementBuilder AddRestInteraction(string resourceType, string interaction)
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

        public ICapabilityStatementBuilder AddRestInteraction(string systemInteraction)
        {
            EnsureArg.IsNotNullOrEmpty(systemInteraction, nameof(systemInteraction));

            _statement.Rest.Server().Interaction.Add(new ResourceInteractionComponent { Code = systemInteraction });

            return this;
        }

        public ICapabilityStatementBuilder AddDefaultRestSearchParams()
        {
            _statement.Rest.Server().SearchParam.Add(new SearchParamComponent { Name = SearchParameterNames.ResourceType, Definition = SearchParameterNames.TypeUri, Type = SearchParamType.Token });

            return this;
        }

        public ICapabilityStatementBuilder AddSearchParams(string resourceType, IEnumerable<SearchParamComponent> searchParameters)
        {
            EnsureArg.IsNotNullOrEmpty(resourceType, nameof(resourceType));
            EnsureArg.IsNotNull(searchParameters, nameof(searchParameters));
            EnsureArg.IsTrue(_modelInfoProvider.IsKnownResource(resourceType), nameof(resourceType), x => GenerateTypeErrorMessage(x, resourceType));

            UpdateRestResourceComponent(resourceType, c =>
            {
                foreach (SearchParamComponent searchParam in searchParameters)
                {
                    // Exclude _type search param under resource
                    if (string.Equals("_type", searchParam.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    c.SearchParam.Add(searchParam);
                }
            });

            return this;
        }

        public ICapabilityStatementBuilder AddDefaultResourceInteractions()
        {
            foreach (string resource in _modelInfoProvider.GetResourceTypeNames())
            {
                // Parameters is a non-persisted resource used to pass information into and back from an operation.
                if (string.Equals(resource, KnownResourceTypes.Parameters, StringComparison.Ordinal))
                {
                    continue;
                }

                AddRestInteraction(resource, TypeRestfulInteraction.Create);
                AddRestInteraction(resource, TypeRestfulInteraction.Read);
                AddRestInteraction(resource, TypeRestfulInteraction.Vread);
                AddRestInteraction(resource, TypeRestfulInteraction.HistoryType);
                AddRestInteraction(resource, TypeRestfulInteraction.HistoryInstance);

                // AuditEvents should not allow Update or Delete
                if (!string.Equals(resource, KnownResourceTypes.AuditEvent, StringComparison.Ordinal))
                {
                    AddRestInteraction(resource, TypeRestfulInteraction.Update);
                    AddRestInteraction(resource, TypeRestfulInteraction.Delete);
                }

                UpdateRestResourceComponent(resource, component =>
                {
                    component.Versioning.Add(ResourceVersionPolicy.NoVersion);
                    component.Versioning.Add(ResourceVersionPolicy.Versioned);
                    component.Versioning.Add(ResourceVersionPolicy.VersionedUpdate);

                    // Create is added for every resource above.
                    component.ConditionalCreate = true;

                    // AuditEvent don't allow update, so no conditional update as well.
                    if (!string.Equals(resource, KnownResourceTypes.AuditEvent, StringComparison.Ordinal))
                    {
                        component.ConditionalUpdate = true;
                    }

                    component.ReadHistory = true;
                    component.UpdateCreate = true;
                });
            }

            AddRestInteraction(SystemRestfulInteraction.HistorySystem);

            return this;
        }

        public ICapabilityStatementBuilder AddDefaultSearchParameters()
        {
            foreach (string resource in _modelInfoProvider.GetResourceTypeNames())
            {
                // Parameters is a non-persisted resource used to pass information into and back from an operation
                if (string.Equals(resource, KnownResourceTypes.Parameters, StringComparison.Ordinal))
                {
                    continue;
                }

                IEnumerable<SearchParameterInfo> searchParams = _searchParameterDefinitionManager.GetSearchParameters(resource);

                if (searchParams.Any())
                {
                    AddSearchParams(resource, searchParams.Select(x => new SearchParamComponent
                    {
                        Name = x.Name,
                        Type = x.Type,
                        Definition = x.Url,
                        Documentation = x.Description,
                    }));

                    AddRestInteraction(resource, TypeRestfulInteraction.SearchType);
                }
            }

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
                    new ProfileReferenceConverter(_modelInfoProvider),
                    new CodingJsonConverter(),
                },
                NullValueHandling = NullValueHandling.Ignore,
            });

            ISourceNode jsonStatement = FhirJsonNode.Parse(json);

            // Using a version specific StructureDefinitionSummaryProvider ensures the metadata to be
            // compatible with the current FhirSerializer/output formatter.
            return jsonStatement.ToTypedElement(_modelInfoProvider.StructureDefinitionSummaryProvider);
        }

        private EnsureOptions GenerateTypeErrorMessage(EnsureOptions options, string resourceType)
        {
            return options.WithMessage($"Unknown resource type {resourceType}");
        }
    }
}
