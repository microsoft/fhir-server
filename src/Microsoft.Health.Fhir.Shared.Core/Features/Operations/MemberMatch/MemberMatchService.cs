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
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions.Parsers;
using Microsoft.Health.Fhir.Core.Models;
using Expression = Microsoft.Health.Fhir.Core.Features.Search.Expressions.Expression;

namespace Microsoft.Health.Fhir.Core.Features.Operations.MemberMatch
{
    public sealed class MemberMatchService : IMemberMatchService
    {
        private readonly Func<IScoped<ISearchService>> _searchServiceFactory;
        private readonly IResourceDeserializer _resourceDeserializer;
        private readonly ISearchIndexer _searchIndexer;
        private readonly IExpressionParser _expressionParser;
        private readonly SearchParameterInfo _coverageBeneficiaryParameter;

        public MemberMatchService(
            Func<IScoped<ISearchService>> searchServiceFactory,
            IResourceDeserializer resourceDeserializer,
            ISearchIndexer searchIndexer,
            ISearchParameterDefinitionManager.SearchableSearchParameterDefinitionManagerResolver searchParameterDefinitionManagerResolver,
            IExpressionParser expressionParser)
        {
            EnsureArg.IsNotNull(searchServiceFactory, nameof(searchServiceFactory));
            EnsureArg.IsNotNull(resourceDeserializer, nameof(resourceDeserializer));
            EnsureArg.IsNotNull(searchIndexer, nameof(searchIndexer));
            EnsureArg.IsNotNull(searchParameterDefinitionManagerResolver, nameof(searchParameterDefinitionManagerResolver));
            EnsureArg.IsNotNull(expressionParser, nameof(expressionParser));
            _searchServiceFactory = searchServiceFactory;
            _resourceDeserializer = resourceDeserializer;
            _searchIndexer = searchIndexer;
            _expressionParser = expressionParser;
            _coverageBeneficiaryParameter = searchParameterDefinitionManagerResolver().GetSearchParameter("Coverage", "beneficiary");
        }

        public async Task<ResourceElement> FindMatch(ResourceElement coverage, ResourceElement patient, CancellationToken cancellationToken)
        {
            var coverageValues = _searchIndexer.Extract(coverage);
            var patientValues = _searchIndexer.Extract(patient);
            var expressions = new List<Expression>();
            var reverseChainExpressions = new List<Expression>();
            var searchOptions = new SearchOptions();
            searchOptions.MaxItemCount = 2;
            searchOptions.Sort = new List<(SearchParameterInfo, SortOrder)>();
            searchOptions.UnsupportedSearchParams = new List<Tuple<string, string>>();

            List<Tuple<string, string>> queryParameters = new List<Tuple<string, string>>();

            // No need to look for more than 2 patients, since we looking for exact match.
            queryParameters.Add(new Tuple<string, string>(KnownQueryParameterNames.Count, "2"));

            foreach (var patientValue in patientValues)
            {
                if (IgnoreInSearch(patientValue))
                {
                    continue;
                }

                queryParameters.Add(new Tuple<string, string>(patientValue.SearchParameter.Code, patientValue.Value.ToString()));
                expressions.Add(_expressionParser.Parse(new[] { "Patient" }, patientValue.SearchParameter.Code, patientValue.Value.ToString()));
            }

            foreach (var coverageValue in coverageValues)
            {
                if (IgnoreInSearch(coverageValue))
                {
                    continue;
                }

                reverseChainExpressions.Add(_expressionParser.Parse(new[] { "Coverage" }, coverageValue.SearchParameter.Code, coverageValue.Value.ToString()));
                queryParameters.Add(new Tuple<string, string>(
                    $"{ExpressionParser.ReverseChainParameter}{KnownResourceTypes.Coverage}:beneficiary:{coverageValue.SearchParameter.Code}", coverageValue.Value.ToString()));
            }

            Expression reverseChainedExpression;
            if (reverseChainExpressions.Count == 1)
            {
                reverseChainedExpression = reverseChainExpressions[0];
            }
            else
            {
                reverseChainedExpression = Expression.And(reverseChainExpressions);
            }

            var expression = Expression.Chained(new string[] { "Coverage" }, _coverageBeneficiaryParameter, new string[] { "Patient" }, true, reverseChainedExpression);
            expressions.Add(expression);
            searchOptions.Expression = Expression.And(expressions);
            using IScoped<ISearchService> search = _searchServiceFactory();

            SearchResult results = await search.Value.SearchAsync(searchOptions, cancellationToken);
            var searchMatchOnly = results.Results.Where(x => x.SearchEntryMode == ValueSets.SearchEntryMode.Match).ToList();
            if (searchMatchOnly.Count > 1)
            {
                throw new MemberMatchMatchingException(Core.Resources.MemberMatchMultipleMatchesFound);
            }

            if (searchMatchOnly.Count == 0)
            {
                throw new MemberMatchMatchingException(Core.Resources.MemberMatchNoMatchFound);
            }

            var match = searchMatchOnly[0];
            var element = _resourceDeserializer.Deserialize(match.Resource);
            var foundPatient = element.ToPoco<Patient>();
            var id = foundPatient.Identifier.Where(x => x.Type.Coding.Exists(x => x.Code == "MB")).FirstOrDefault();
            if (id == null)
            {
                throw new MemberMatchMatchingException(Core.Resources.MemberMatchNoMatchFound);
            }

            var resultPatient = patient.ToPoco<Patient>();
            var resultId = new Identifier(id.System, id.Value);
            resultId.Type = new CodeableConcept("http://terminology.hl7.org/CodeSystem/v2-0203", "UMB", "Member Match");
            resultPatient.Identifier.Add(resultId);
            var result = resultPatient.ToResourceElement();
            return result;
        }

        private static bool IgnoreInSearch(SearchIndexEntry searchEntry) =>
         searchEntry.SearchParameter.Code == SearchParameterNames.Id || searchEntry.SearchParameter.Type == ValueSets.SearchParamType.Reference;
    }
}
