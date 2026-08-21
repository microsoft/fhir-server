// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser
{
    public class SqlSearchParameterDefinitionManager
    {
        private readonly SearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly ISqlServerFhirModel _sqlServerFhirModel;

        public SqlSearchParameterDefinitionManager(SearchParameterDefinitionManager searchParameterDefinitionManager, ISqlServerFhirModel sqlServerFhirModel)
        {
            ArgumentNullException.ThrowIfNull(searchParameterDefinitionManager);
            ArgumentNullException.ThrowIfNull(sqlServerFhirModel);

            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _sqlServerFhirModel = sqlServerFhirModel;
        }

        public SearchParameterIdWrapper GetByCode(string code, short resourceType)
        {
            if (resourceType == 0)
            {
                // If the resource type is 0, we are searching for a search parameter that is applicable to all resource types.
                resourceType = 1;
            }

            return GetByCode(code, _sqlServerFhirModel.GetResourceTypeName(resourceType));
        }

        public SearchParameterIdWrapper GetByCode(string code, string resourceType)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                throw new ArgumentException("Code cannot be null or whitespace.", nameof(code));
            }

            if (code.Contains(':', StringComparison.OrdinalIgnoreCase))
            {
                code = code.Split(':', 2, StringSplitOptions.None)[0];
            }

            var searchParameterInfo = _searchParameterDefinitionManager.GetSearchParameter(resourceType, code);
            return new SearchParameterIdWrapper()
            {
                SearchParameterInfo = searchParameterInfo,
                Id = _sqlServerFhirModel.GetSearchParamId(searchParameterInfo?.Url),
            };
        }

        public SearchParameterIdWrapper GetByUrl(Uri url)
        {
            ArgumentNullException.ThrowIfNull(url);

            var searchParameterInfo = _searchParameterDefinitionManager.GetSearchParameter(url.OriginalString);
            return new SearchParameterIdWrapper()
            {
                SearchParameterInfo = searchParameterInfo,
                Id = _sqlServerFhirModel.GetSearchParamId(searchParameterInfo?.Url),
            };
        }

        public IList<SearchParameterIdWrapper> GetByResourceType(short resourceType)
        {
            if (resourceType == 0)
            {
                // If the resource type is 0, we are searching for a search parameter that is applicable to all resource types.
                resourceType = 1;
            }

            return GetByResourceType(_sqlServerFhirModel.GetResourceTypeName(resourceType));
        }

        public IList<SearchParameterIdWrapper> GetByResourceType(string resourceType)
        {
            var parameters = _searchParameterDefinitionManager.GetSearchParameters(resourceType);
            return parameters.Select(x => new SearchParameterIdWrapper() { SearchParameterInfo = x, Id = _sqlServerFhirModel.GetSearchParamId(x.Url) }).ToList();
        }

        public SearchParamType? GetParameterType(string code, short resourceType)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return null;
            }

            var parameter = GetByCode(code, resourceType);
            return parameter?.SearchParameterInfo.Type;
        }
    }
}
