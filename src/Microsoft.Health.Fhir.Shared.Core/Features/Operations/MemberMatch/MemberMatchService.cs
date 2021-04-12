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
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions.Parsers;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.MemberMatch
{
    public sealed class MemberMatchService : IMemberMatchService
    {
        private readonly Func<IScoped<ISearchService>> _searchServiceFactory;
        private readonly IResourceDeserializer _resourceDeserializer;
        private readonly ISearchIndexer _searchIndexer;

        public MemberMatchService(
            Func<IScoped<ISearchService>> searchServiceFactory,
            IResourceDeserializer resourceDeserializer,
            ISearchIndexer searchIndexer)
        {
            EnsureArg.IsNotNull(searchServiceFactory, nameof(searchServiceFactory));
            EnsureArg.IsNotNull(resourceDeserializer, nameof(resourceDeserializer));
            EnsureArg.IsNotNull(searchIndexer, nameof(searchIndexer));
            _searchServiceFactory = searchServiceFactory;
            _resourceDeserializer = resourceDeserializer;
            _searchIndexer = searchIndexer;
        }

        public async Task<ResourceElement> FindMatch(ResourceElement coverage, ResourceElement patient, CancellationToken cancellationToken)
        {
            var coverageValues = _searchIndexer.Extract(coverage);
            var patientValues = _searchIndexer.Extract(patient);

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
            }

            foreach (var coverageValue in coverageValues)
            {
                if (IgnoreInSearch(coverageValue))
                {
                    continue;
                }

                queryParameters.Add(new Tuple<string, string>(
                    $"{ExpressionParser.ReverseChainParameter}{KnownResourceTypes.Coverage}:beneficiary:{coverageValue.SearchParameter.Code}", coverageValue.Value.ToString()));
            }

            using IScoped<ISearchService> search = _searchServiceFactory();

            SearchResult results = await search.Value.SearchAsync(KnownResourceTypes.Patient, queryParameters, cancellationToken);
            if (results.Results.Count() > 1)
            {
                throw new MemberMatchMatchingException(Core.Resources.MemberMatchMultipleMatchesFound);
            }

            if (!results.Results.Any())
            {
                throw new MemberMatchMatchingException(Core.Resources.MemberMatchNoMatchFound);
            }

            var match = results.Results.First();
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
