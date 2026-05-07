// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Introspection;
using Hl7.FhirPath;
using Hl7.FhirPath.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Search.Parameters
{
    public class SearchParameterSupportResolver : ISearchParameterSupportResolver
    {
        private readonly ITypedElementToSearchValueConverterManager _searchValueConverterManager;
        private static readonly FhirPathCompiler _compiler = new FhirPathCompiler();
        private const string _codeOfTName = "codeOfT";

        public SearchParameterSupportResolver(ITypedElementToSearchValueConverterManager searchValueConverterManager)
        {
            EnsureArg.IsNotNull(searchValueConverterManager, nameof(searchValueConverterManager));

            _searchValueConverterManager = searchValueConverterManager;
        }

        public (bool Supported, bool IsPartiallySupported, bool IsDateOnly, bool IsScalarTemporal) IsSearchParameterSupported(SearchParameterInfo parameterInfo)
        {
            EnsureArg.IsNotNull(parameterInfo, nameof(parameterInfo));

            if (string.IsNullOrWhiteSpace(parameterInfo.Expression))
            {
                return (false, false, false, false);
            }

            Expression parsed = _compiler.Parse(parameterInfo.Expression);
            if (parameterInfo.Component != null && parameterInfo.Component.Any(x => x.ResolvedSearchParameter == null))
            {
                return (false, false, false, false);
            }

            (SearchParamType Type, Expression, Uri DefinitionUrl)[] componentExpressions = parameterInfo.Component
                ?.Select(x => (x.ResolvedSearchParameter.Type,
                    _compiler.Parse(x.Expression),
                    x.DefinitionUrl))
                .ToArray();

            List<string> resourceTypes = (parameterInfo.TargetResourceTypes ?? Enumerable.Empty<string>()).Concat(parameterInfo.BaseResourceTypes ?? Enumerable.Empty<string>()).ToList();

            if (!resourceTypes.Any())
            {
                throw new NotSupportedException("No target resources defined.");
            }

            bool isSimpleDateParameter =
                parameterInfo.Type == SearchParamType.Date &&
                (parameterInfo.Component == null || parameterInfo.Component.Count == 0);
            // Both flags start true only when this is a simple Date parameter (no components).
            // Each is AND-reduced across all per-resource type resolutions inside the loop below.
            // A single non-qualifying result, an empty resolution, or any early return forces both to false.
            bool allResolutionsAreDateOnly = isSimpleDateParameter;
            bool allResolutionsAreScalarTemporal = isSimpleDateParameter;

            foreach (var resource in resourceTypes)
            {
                SearchParameterTypeResult[] results = SearchParameterToTypeResolver.Resolve(
                    resource,
                    (parameterInfo.Type, parsed, parameterInfo.Url),
                    componentExpressions).ToArray();

                if (allResolutionsAreDateOnly)
                {
                    if (results.Length == 0 ||
                        !results.All(r => string.Equals(r.FhirNodeType, "date", StringComparison.OrdinalIgnoreCase)))
                    {
                        allResolutionsAreDateOnly = false;
                    }
                }

                if (allResolutionsAreScalarTemporal)
                {
                    if (results.Length == 0 ||
                        !results.All(r => IsScalarTemporalNodeType(r.FhirNodeType)))
                    {
                        allResolutionsAreScalarTemporal = false;
                    }
                }

                var converters = results
                    .Select(result => (
                        result,
                        hasConverter: _searchValueConverterManager.TryGetConverter(
                            GetBaseType(result.ClassMapping),
                            TypedElementSearchIndexer.GetSearchValueTypeForSearchParamType(result.SearchParamType),
                            out ITypedElementToSearchValueConverter converter),
                        converter))
                    .ToArray();

                if (!converters.Any())
                {
                    return (false, false, false, false);
                }

                if (!converters.All(x => x.hasConverter))
                {
                    bool partialSupport = converters.Any(x => x.hasConverter);
                    return (partialSupport, partialSupport, false, false);
                }
            }

            string GetBaseType(ClassMapping classMapping)
            {
                return classMapping.IsCodeOfT ? _codeOfTName : classMapping.Name;
            }

            static bool IsScalarTemporalNodeType(string fhirNodeType)
            {
                return string.Equals(fhirNodeType, "date", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(fhirNodeType, "dateTime", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(fhirNodeType, "instant", StringComparison.OrdinalIgnoreCase);
            }

            return (true, false, allResolutionsAreDateOnly, allResolutionsAreScalarTemporal);
        }
    }
}
