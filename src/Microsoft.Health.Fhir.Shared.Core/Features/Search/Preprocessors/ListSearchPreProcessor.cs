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
using MediatR.Pipeline;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.PreProcessors
{
    /// <summary>
    /// This behavior looks for searches that use _list then resolves the list and rewrites the query with the resource _ids
    /// </summary>
    public class ListSearchPreProcessor : IRequestPreProcessor<SearchResourceRequest>
    {
        private const string _listParameter = "_list";
        private const string _idParameter = "_id";
        private readonly IScoped<IFhirDataStore> _dataStore;
        private readonly ResourceDeserializer _deserializer;
        private readonly IReferenceSearchValueParser _referenceSearchValueParser;

        public ListSearchPreProcessor(
            IScoped<IFhirDataStore> dataStore,
            ResourceDeserializer deserializer,
            IReferenceSearchValueParser referenceSearchValueParser)
        {
            EnsureArg.IsNotNull(dataStore, nameof(dataStore));
            EnsureArg.IsNotNull(deserializer, nameof(deserializer));
            EnsureArg.IsNotNull(referenceSearchValueParser, nameof(referenceSearchValueParser));

            _dataStore = dataStore;
            _deserializer = deserializer;
            _referenceSearchValueParser = referenceSearchValueParser;
        }

        public async Task Process(SearchResourceRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            Tuple<string, string> listParameter = request.Queries
                .FirstOrDefault(x => string.Equals(x.Item1, _listParameter, StringComparison.Ordinal));

            // Remove the 'list' params from the queries, to be later replaced with specific ids of resources
            IEnumerable<Tuple<string, string>> query = request.Queries.Except(new[] { listParameter });

            // if _list was not requested, or _list was requested with invalid value. continue
            if ((listParameter == null) || string.IsNullOrWhiteSpace(listParameter.Item2))
            {
                request.Queries = query.ToArray();
                return;
            }

            ResourceWrapper listWrapper = await _dataStore.Value.GetAsync(new ResourceKey<Hl7.Fhir.Model.List>(listParameter.Item2), cancellationToken);

            if (listWrapper == null)
            {
                MakeAlwaysFalseQuery(ref query);
                request.Queries = query.ToArray();
                return;
            }

            ResourceElement list = _deserializer.Deserialize(listWrapper);

            IEnumerable<ReferenceSearchValue> references = list.ToPoco<Hl7.Fhir.Model.List>()
                .Entry
                .Where(x => x.Deleted != true)
                .Select(x => _referenceSearchValueParser.Parse(x.Item.Reference))
                .Where(x => string.IsNullOrWhiteSpace(request.ResourceType) ||
                            string.Equals(request.ResourceType, x.ResourceType, StringComparison.Ordinal))
                .ToArray();

            if (references.Any())
            {
                query = query.Concat(new[] { Tuple.Create(_idParameter, string.Join(",", references.Select(x => x.ResourceId))) });
            }
            else
            {
                MakeAlwaysFalseQuery(ref query);
            }

            request.Queries = query.ToArray();
        }

        private void MakeAlwaysFalseQuery(ref IEnumerable<Tuple<string, string>> query)
        {
            // when asking the id to be both 1 and 2, we make sure no results will return.
            // This is a workaround to the fact the preprocessor does not allow to
            // return the empty results directly...
            query = query.Concat(new[] { new Tuple<string, string>("_id", "one") });
            query = query.Concat(new[] { new Tuple<string, string>("_id", "two") });
        }
    }
}