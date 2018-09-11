// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Extensions;

namespace Microsoft.Health.Fhir.Core.Features.Search.Legacy
{
    /// <summary>
    /// Provides a way to get search parameter definition.
    /// </summary>
    public class SearchParamDefinitionManager : ISearchParamDefinitionManager
    {
        private static Dictionary<(string, string), ModelInfo.SearchParamDefinition> definitionMapping =
            ModelInfo.SearchParameters.ToDictionary(
                definition => ValueTuple.Create(definition.Resource, definition.Name),
                definition => definition);

        private static Dictionary<string, SearchParamType> _genericResourceSearchParams =
            new Dictionary<string, SearchParamType>
            {
                { SearchParamNames.Content, SearchParamType.String },
                { SearchParamNames.Id, SearchParamType.Token },
                { SearchParamNames.LastUpdated, SearchParamType.Date },
                { SearchParamNames.Profile, SearchParamType.Uri },
                { SearchParamNames.Query, SearchParamType.Token },
                { SearchParamNames.Security, SearchParamType.Token },
                { SearchParamNames.Tag, SearchParamType.Token },
            };

        private static Dictionary<string, SearchParamType> _genericDomainResourceSearchParams =
            new Dictionary<string, SearchParamType>
            {
                { SearchParamNames.Text, SearchParamType.String },
            };

        /// <inheritdoc />
        public SearchParamType GetSearchParamType(Type resourceType, string paramName)
        {
            EnsureArg.IsNotNull(resourceType, nameof(resourceType));
            EnsureArg.IsTrue(typeof(Resource).IsAssignableFrom(resourceType), nameof(resourceType));
            EnsureArg.IsNotNullOrWhiteSpace(paramName, nameof(paramName));

            // Check for generic search parameters that apply to all resource types
            if (_genericResourceSearchParams.TryGetValue(paramName, out SearchParamType resourceSearchParam))
            {
                return resourceSearchParam;
            }

            // Check for generic search parameters that apply to domain resource types
            if (typeof(DomainResource).IsAssignableFrom(resourceType) && _genericDomainResourceSearchParams.TryGetValue(paramName, out SearchParamType domainResourceSearchParam))
            {
                return domainResourceSearchParam;
            }

            // Check for search parameters that apply to specific resources
            ModelInfo.SearchParamDefinition definition = FindSearchParamDefinition(resourceType, paramName);

            return definition.Type;
        }

        /// <inheritdoc />
        public IReadOnlyCollection<Type> GetReferenceTargetResourceTypes(Type resourceType, string paramName)
        {
            EnsureArg.IsNotNull(resourceType, nameof(resourceType));
            EnsureArg.IsTrue(typeof(Resource).IsAssignableFrom(resourceType), nameof(resourceType));
            EnsureArg.IsNotNullOrWhiteSpace(paramName, nameof(paramName));

            ModelInfo.SearchParamDefinition definition = FindSearchParamDefinition(resourceType, paramName);

            if (definition.Type != SearchParamType.Reference)
            {
                throw new InvalidOperationException(
                    string.Format(Core.Resources.SpecifiedResourceTypeIsNotReferenceType, resourceType.Name));
            }

            return definition.Target.Select(type => type.ToResourceModelType()).ToArray();
        }

        private static ModelInfo.SearchParamDefinition FindSearchParamDefinition(Type resourceType, string paramName)
        {
            if (!ModelInfo.FhirCsTypeToString.TryGetValue(resourceType, out string resourceTypeName))
            {
                // Somehow the type specified is a Resource but it does not exist in the ModelInfo class.
                // The likely case is that we have custom resource that's not in the STU3 library.
                // We will support that later when we actually need it.
                throw new ResourceNotSupportedException(resourceType);
            }

            var key = ValueTuple.Create(resourceTypeName, paramName);

            if (!definitionMapping.TryGetValue(key, out ModelInfo.SearchParamDefinition definition))
            {
                // The search param is not supported on this resource type.
                throw new SearchParameterNotSupportedException(resourceType, paramName);
            }

            return definition;
        }
    }
}
