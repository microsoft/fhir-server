// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Conformance;

namespace Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues
{
    public partial class ResourceTypeManifestManager : IResourceTypeManifestManager, IProvideCapability
    {
        private readonly ISearchParamFactory _searchParamFactory;
        private readonly ISearchParamDefinitionManager _searchParamDefinitionManager;
        private readonly ILogger<ResourceTypeManifestManager> _logger;

        private readonly Dictionary<Type, ResourceTypeManifest> _manifestLookup;
        private readonly ResourceTypeManifest _genericResourceTypeManifest;

        public ResourceTypeManifestManager(ISearchParamFactory searchParamFactory, ISearchParamDefinitionManager searchParamManager, ILogger<ResourceTypeManifestManager> logger)
        {
            EnsureArg.IsNotNull(logger, nameof(logger));

            _searchParamFactory = searchParamFactory;
            _searchParamDefinitionManager = searchParamManager;
            _logger = logger;

            _manifestLookup = new Dictionary<Type, ResourceTypeManifest>
            {
                { typeof(Appointment), CreateAppointmentManifest() },
                { typeof(Communication), CreateCommunicationManifest() },
                { typeof(Condition), CreateConditionManifest() },
                { typeof(Immunization), CreateImmunizationManifest() },
                { typeof(Observation), CreateObservationManifest() },
                { typeof(Organization), CreateOrganizationManifest() },
                { typeof(Patient), CreatePatientManifest() },
                { typeof(Practitioner), CreatePractitionerManifest() },
                { typeof(Questionnaire), CreateQuestionnaireManifest() },
                { typeof(QuestionnaireResponse), CreateQuestionnaireResponseManifest() },
                { typeof(RelatedPerson), CreateRelatedPersonManifest() },
                { typeof(ValueSet), CreateValueSetManifest() },
            };

            // Generic properties can be searched on any resource without knowing the type,
            // this field holds values for this purpose.
            _genericResourceTypeManifest = CreateResourceTypeManifestBuilder<Resource>().ToManifest();

            // The following is temporary to enable default properties for all other resources
            // that do not have search parameters specifically created for them
            var genericManifest = GetType()
                .GetMethod(nameof(CreateResourceTypeManifestBuilder), BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var resource in ModelInfo.FhirCsTypeToString.Keys.Except(_manifestLookup.Keys).Where(x => typeof(Resource).IsAssignableFrom(x)))
            {
                var method = genericManifest.MakeGenericMethod(resource);
                dynamic builder = method.Invoke(this, null);
                _manifestLookup.Add(resource, builder.ToManifest());
            }
        }

        public ResourceTypeManifest GetManifest(Type resourceType)
        {
            EnsureArg.IsNotNull(resourceType, nameof(resourceType));

            if (_manifestLookup.TryGetValue(resourceType, out ResourceTypeManifest manifest))
            {
                return manifest;
            }

            throw new ResourceNotSupportedException(resourceType);
        }

        public ResourceTypeManifest GetGenericManifest()
        {
            return _genericResourceTypeManifest;
        }

        public void Build(ListedCapabilityStatement statement)
        {
            foreach (var manifest in _manifestLookup)
            {
                if (Enum.TryParse(manifest.Key.Name, out ResourceType resourceType))
                {
                    IEnumerable<CapabilityStatement.SearchParamComponent> searchParams = manifest.Value.SupportedSearchParams.Select(
                        searchParam => new CapabilityStatement.SearchParamComponent
                        {
                            Name = searchParam.ParamName,
                            Type = searchParam.ParamType,
                        });

                    statement.TryAddSearchParams(resourceType, searchParams);
                    statement.TryAddRestInteraction(resourceType, CapabilityStatement.TypeRestfulInteraction.SearchType);
                }
                else
                {
                    _logger.LogWarning($@"Could not parse ""{manifest.Key.Name}"" as Hl7.Fhir.Model.ResourceType.");
                }
            }
        }

        private ResourceTypeManifestBuilder<T> CreateResourceTypeManifestBuilder<T>()
            where T : Resource
        {
            var builder = new ResourceTypeManifestBuilder<T>(_searchParamFactory);

            // Adds the common search parameters.
            builder.AddTokenSearchParam(SearchParamNames.Id, r => r.Id);
            builder.AddDateTimeSearchParam(SearchParamNames.LastUpdated, r => r.Meta?.LastUpdatedElement);
            builder.AddUriSearchParam(SearchParamNames.Profile, r => r.Meta?.Profile);
            builder.AddTokenSearchParam(SearchParamNames.Security, r => r.Meta?.Security);
            builder.AddTokenSearchParam(SearchParamNames.Tag, r => r.Meta?.Tag);

            return builder;
        }
    }
}
