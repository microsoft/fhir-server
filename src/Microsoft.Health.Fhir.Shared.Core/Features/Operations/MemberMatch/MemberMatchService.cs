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
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions.Parsers;
using Microsoft.Health.Fhir.Core.Models;
using Expression = Microsoft.Health.Fhir.Core.Features.Search.Expressions.Expression;
using SortOrder = Microsoft.Health.Fhir.Core.Features.Search.SortOrder;

namespace Microsoft.Health.Fhir.Core.Features.Operations.MemberMatch
{
    public sealed class MemberMatchService : IMemberMatchService
    {
        private readonly Func<IScoped<ISearchService>> _searchServiceFactory;
        private readonly IResourceDeserializer _resourceDeserializer;
        private readonly ISearchIndexer _searchIndexer;
        private readonly IExpressionParser _expressionParser;
        private readonly SearchParameterInfo _coverageBeneficiaryParameter;
        private readonly SearchParameterInfo _resourceTypeSearchParameter;
        private readonly ILogger<MemberMatchService> _logger;

        public MemberMatchService(
            Func<IScoped<ISearchService>> searchServiceFactory,
            IResourceDeserializer resourceDeserializer,
            ISearchIndexer searchIndexer,
            ISearchParameterDefinitionManager.SearchableSearchParameterDefinitionManagerResolver searchParameterDefinitionManagerResolver,
            IExpressionParser expressionParser,
            ILogger<MemberMatchService> logger)
        {
            EnsureArg.IsNotNull(searchServiceFactory, nameof(searchServiceFactory));
            EnsureArg.IsNotNull(resourceDeserializer, nameof(resourceDeserializer));
            EnsureArg.IsNotNull(searchIndexer, nameof(searchIndexer));
            EnsureArg.IsNotNull(searchParameterDefinitionManagerResolver, nameof(searchParameterDefinitionManagerResolver));
            EnsureArg.IsNotNull(expressionParser, nameof(expressionParser));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _searchServiceFactory = searchServiceFactory;
            _resourceDeserializer = resourceDeserializer;
            _searchIndexer = searchIndexer;
            _expressionParser = expressionParser;
            var searchParameterDefinition = searchParameterDefinitionManagerResolver();
            _coverageBeneficiaryParameter = searchParameterDefinition.GetSearchParameter("Coverage", "beneficiary");
            _resourceTypeSearchParameter = searchParameterDefinition.GetSearchParameter(ResourceType.Resource.ToString(), SearchParameterNames.ResourceType);
            _logger = logger;
        }

        public async Task<ResourceElement> FindMatch(ResourceElement coverage, ResourceElement patient, CancellationToken cancellationToken)
        {
            var searchOptions = new SearchOptions();
            searchOptions.MaxItemCount = 2;
            searchOptions.Sort = new List<(SearchParameterInfo, SortOrder)>();
            searchOptions.UnsupportedSearchParams = new List<Tuple<string, string>>();
            searchOptions.Expression = CreateSearchExpression(coverage, patient);

            SearchResult results = null;
            try
            {
                using IScoped<ISearchService> search = _searchServiceFactory();
                results = await search.Value.SearchAsync(searchOptions, cancellationToken);
            }
            catch (InvalidSearchOperationException ex)
            {
                _logger.LogError(ex, $"{nameof(InvalidSearchOperationException)} in MemberMatch service.");
                throw;
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("The query processor ran out of internal resources and could not produce a query plan.", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogError(ex, $"{nameof(SqlQueryPlanException)} in MemberMatch service.");
                    throw;
                }

                _logger.LogError(ex, "Generic problem in MemberMatch service.");
                throw new MemberMatchMatchingException(Core.Resources.GenericMemberMatch);
            }

            return CreatePatientWithIdentity(patient, results);
        }

        private ResourceElement CreatePatientWithIdentity(ResourceElement patient, SearchResult results)
        {
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
            var id = foundPatient.Identifier.Where(x => x.Type != null && x.Type.Coding != null && x.Type.Coding.Exists(x => x.Code == "MB")).FirstOrDefault();
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

        private Expression CreateSearchExpression(ResourceElement coverage, ResourceElement patient)
        {
            var coverageValues = _searchIndexer.Extract(coverage);
            var patientValues = _searchIndexer.Extract(patient);
            var expressions = new List<Expression>();
            var reverseChainExpressions = new List<Expression>();
            expressions.Add(Expression.SearchParameter(_resourceTypeSearchParameter, Expression.StringEquals(FieldName.TokenCode, null, KnownResourceTypes.Patient, false)));
            foreach (var patientValue in patientValues)
            {
                if (IgnoreInSearch(patientValue))
                {
                    continue;
                }

                var modifier = string.Empty;
                if (patientValue.SearchParameter.Type == ValueSets.SearchParamType.String)
                {
                    modifier = ":exact";
                }

                expressions.Add(_expressionParser.Parse(new[] { KnownResourceTypes.Patient }, patientValue.SearchParameter.Code + modifier, patientValue.Value.ToString()));
            }

            foreach (var coverageValue in coverageValues)
            {
                if (IgnoreInSearch(coverageValue))
                {
                    continue;
                }

                var modifier = string.Empty;
                if (coverageValue.SearchParameter.Type == ValueSets.SearchParamType.String)
                {
                    modifier = ":exact";
                }

                reverseChainExpressions.Add(_expressionParser.Parse(new[] { KnownResourceTypes.Coverage }, coverageValue.SearchParameter.Code + modifier, coverageValue.Value.ToString()));
            }

            if (reverseChainExpressions.Count != 0)
            {
                Expression reverseChainedExpression;
                if (reverseChainExpressions.Count == 1)
                {
                    reverseChainedExpression = reverseChainExpressions[0];
                }
                else
                {
                    reverseChainedExpression = Expression.And(reverseChainExpressions);
                }

                var expression = Expression.Chained(new[] { KnownResourceTypes.Coverage }, _coverageBeneficiaryParameter, new[] { KnownResourceTypes.Patient }, true, reverseChainedExpression);
                expressions.Add(expression);
            }

            return Expression.And(expressions);
        }

        private static bool IgnoreInSearch(SearchIndexEntry searchEntry) =>
         searchEntry.SearchParameter.Code == SearchParameterNames.Id
            || searchEntry.SearchParameter.Type == ValueSets.SearchParamType.Reference
            || !searchEntry.SearchParameter.IsSearchable;
    }
}
