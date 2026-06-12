// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Utility;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions.Parsers;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using SearchModifierCode = Microsoft.Health.Fhir.ValueSets.SearchModifierCode;
using SearchParamType = Microsoft.Health.Fhir.ValueSets.SearchParamType;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    public class SearchParameterValueSetExpander : ISearchParameterQueryParameterExpander
    {
        private const string InModifierSuffix = ":in";
        private const string EmptyValueSetTokenSystem = "urn:microsoft:fhir:search:empty-valueset";
        private const string EmptyValueSetTokenCode = "__empty_valueset__";

        private readonly ITerminologyServiceProxy _terminologyServiceProxy;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;

        public SearchParameterValueSetExpander(
            ITerminologyServiceProxy terminologyServiceProxy,
            ISearchParameterDefinitionManager.SearchableSearchParameterDefinitionManagerResolver searchParameterDefinitionManagerResolver)
        {
            EnsureArg.IsNotNull(terminologyServiceProxy, nameof(terminologyServiceProxy));
            EnsureArg.IsNotNull(searchParameterDefinitionManagerResolver, nameof(searchParameterDefinitionManagerResolver));

            _terminologyServiceProxy = terminologyServiceProxy;
            _searchParameterDefinitionManager = searchParameterDefinitionManagerResolver();
        }

        public async Task<IReadOnlyList<Tuple<string, string>>> ExpandAsync(
            string resourceType,
            IReadOnlyList<Tuple<string, string>> queryParameters,
            CancellationToken cancellationToken)
        {
            if (queryParameters == null || queryParameters.Count == 0)
            {
                return queryParameters;
            }

            List<Tuple<string, string>> expandedParameters = null;

            for (int i = 0; i < queryParameters.Count; i++)
            {
                Tuple<string, string> queryParameter = queryParameters[i];
                Tuple<string, string> expandedParameter = await ExpandParameterAsync(resourceType, queryParameter, cancellationToken);

                if (expandedParameter != queryParameter && expandedParameters == null)
                {
                    expandedParameters = queryParameters.Take(i).ToList();
                }

                expandedParameters?.Add(expandedParameter);
            }

            return expandedParameters ?? queryParameters;
        }

        private async Task<Tuple<string, string>> ExpandParameterAsync(
            string resourceType,
            Tuple<string, string> queryParameter,
            CancellationToken cancellationToken)
        {
            if (queryParameter == null ||
                string.IsNullOrWhiteSpace(queryParameter.Item1) ||
                string.IsNullOrWhiteSpace(queryParameter.Item2) ||
                !queryParameter.Item1.EndsWith(InModifierSuffix, StringComparison.Ordinal))
            {
                return queryParameter;
            }

            string searchParameterCode = queryParameter.Item1.Substring(0, queryParameter.Item1.Length - InModifierSuffix.Length);
            if (ExpressionParser.ContainsChainOrReverseParameter(searchParameterCode) ||
                !_searchParameterDefinitionManager.TryGetSearchParameter(resourceType, searchParameterCode, out SearchParameterInfo searchParameter) ||
                searchParameter.Type != SearchParamType.Token)
            {
                return queryParameter;
            }

            IReadOnlyList<string> valueSetUrls = queryParameter.Item2.SplitByOrSeparator();
            var tokens = new List<TokenSearchValue>();

            foreach (string valueSetUrl in valueSetUrls)
            {
                ValueSet expandedValueSet = await ExpandValueSetAsync(valueSetUrl, cancellationToken);
                tokens.AddRange(GetTokenSearchValues(expandedValueSet.Expansion?.Contains));
            }

            string expandedValue = tokens.Count == 0
                ? new TokenSearchValue(EmptyValueSetTokenSystem, EmptyValueSetTokenCode, null).ToString()
                : string.Join(",", tokens.Select(token => token.ToString()).Distinct(StringComparer.Ordinal));

            return Tuple.Create(searchParameterCode, expandedValue);
        }

        private async Task<ValueSet> ExpandValueSetAsync(string valueSetUrl, CancellationToken cancellationToken)
        {
            var parameters = new[]
            {
                Tuple.Create(TerminologyOperationParameterNames.Expand.Url, valueSetUrl),
            };

            var resource = await _terminologyServiceProxy.ExpandAsync(parameters, null, cancellationToken);

            if (resource?.ToPoco() is ValueSet valueSet)
            {
                return valueSet;
            }

            throw new InvalidSearchOperationException(
                string.Format(Core.Resources.ModifierNotSupported, SearchModifierCode.In.GetLiteral(), valueSetUrl));
        }

        private static IEnumerable<TokenSearchValue> GetTokenSearchValues(IEnumerable<ValueSet.ContainsComponent> contains)
        {
            if (contains == null)
            {
                yield break;
            }

            foreach (ValueSet.ContainsComponent component in contains)
            {
                if (!string.IsNullOrWhiteSpace(component.Code))
                {
                    yield return new TokenSearchValue(component.System, component.Code, null);
                }

                foreach (TokenSearchValue nestedToken in GetTokenSearchValues(component.Contains))
                {
                    yield return nestedToken;
                }
            }
        }
    }
}
