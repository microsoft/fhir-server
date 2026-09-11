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
            _resourceTypeSearchParameter = searchParameterDefinition.GetSearchParameter(KnownResourceTypes.Resource, SearchParameterNames.ResourceType);
            _logger = logger;
        }

        public async Task<ResourceElement> FindMatch(ResourceElement coverage, ResourceElement patient, CancellationToken cancellationToken)
        {
            var searchOptions = new SearchOptions();
            searchOptions.MaxItemCount = 2;
            searchOptions.Sort = new List<(SearchParameterInfo, SortOrder)>();
            searchOptions.UnsupportedSearchParams = new List<Tuple<string, string>>();
            searchOptions.QueryParams = BuildQueryParams(coverage, patient);

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

        private Dictionary<string, IList<string>> BuildQueryParams(ResourceElement coverage, ResourceElement patient)
        {
            var queryParams = new Dictionary<string, IList<string>>();

            // Resource type filter - search for Patient resources
            queryParams["_type"] = new List<string> { KnownResourceTypes.Patient };

            // Add patient search parameters
            IReadOnlyCollection<SearchIndexEntry> patientValues = _searchIndexer.Extract(patient);
            foreach (SearchIndexEntry patientValue in patientValues)
            {
                if (IgnoreInSearch(patientValue))
                {
                    continue;
                }

                var paramName = patientValue.SearchParameter.Code;
                if (patientValue.SearchParameter.Type == ValueSets.SearchParamType.String)
                {
                    paramName += ":exact";
                }

                var value = patientValue.Value.ToString();
                if (queryParams.TryGetValue(paramName, out var existingValues))
                {
                    existingValues.Add(value);
                }
                else
                {
                    queryParams[paramName] = new List<string> { value };
                }
            }

            // Add coverage search parameters as reverse chain (_has:Coverage:beneficiary:<param>=<value>)
            IReadOnlyCollection<SearchIndexEntry> coverageValues = _searchIndexer.Extract(coverage);
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

                var hasKey = $"_has:Coverage:beneficiary:{coverageValue.SearchParameter.Code}{modifier}";
                var value = coverageValue.Value.ToString();
                if (queryParams.TryGetValue(hasKey, out var existingValues))
                {
                    existingValues.Add(value);
                }
                else
                {
                    queryParams[hasKey] = new List<string> { value };
                }
            }

            return queryParams;
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

        private static bool IgnoreInSearch(SearchIndexEntry searchEntry) =>
         searchEntry.SearchParameter.Code == SearchParameterNames.Id
            || searchEntry.SearchParameter.Type == ValueSets.SearchParamType.Reference
            || !searchEntry.SearchParameter.IsSearchable;
    }
}
