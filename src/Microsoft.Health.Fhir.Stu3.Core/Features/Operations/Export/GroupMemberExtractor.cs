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
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    public class GroupMemberExtractor : IGroupMemberExtractor
    {
        private readonly IFhirDataStore _fhirDataStore;
        private readonly ResourceDeserializer _resourceDeserializer;
        private readonly Func<IScoped<ISearchService>> _searchServiceFactory;

        public GroupMemberExtractor(
            IFhirDataStore fhirDataStore,
            ResourceDeserializer resourceDeserializer,
            Func<IScoped<ISearchService>> searchServiceFactory)
        {
            EnsureArg.IsNotNull(fhirDataStore, nameof(fhirDataStore));
            EnsureArg.IsNotNull(resourceDeserializer, nameof(resourceDeserializer));
            EnsureArg.IsNotNull(searchServiceFactory, nameof(searchServiceFactory));

            _fhirDataStore = fhirDataStore;
            _resourceDeserializer = resourceDeserializer;
            _searchServiceFactory = searchServiceFactory;
        }

        public async Task<List<Tuple<string, string>>> GetGroupMembers(string groupId, CancellationToken cancellationToken)
        {
            var groupResource = await _fhirDataStore.GetAsync(new ResourceKey(KnownResourceTypes.Group, groupId), cancellationToken);

            var group = _resourceDeserializer.Deserialize(groupResource);
            var groupContents = group.ToPoco<Group>().Member;

            var members = new List<Tuple<string, string>>();

            using (IScoped<ISearchService> searchService = _searchServiceFactory())
            {
                foreach (Group.MemberComponent member in groupContents)
                {
                    var queryParametersList = new List<Tuple<string, string>>()
                    {
                        Tuple.Create(KnownQueryParameterNames.Id, member.Entity.Reference),
                    };

                    var searchResult = await searchService.Value.SearchAsync(
                        resourceType: null,
                        queryParametersList,
                        cancellationToken);

                    if (searchResult.Results.Count() > 1)
                    {
                        // throw exception...
                    }
                    else if (searchResult.Results.Count() == 0)
                    {
                        // throw exception...
                    }

                    members.Add(new Tuple<string, string>(member.Entity.Reference, searchResult.Results.First().Resource.ResourceTypeName));
                }
            }

            return members;
        }
    }
}
