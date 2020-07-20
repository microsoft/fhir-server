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
    /// <summary>
    /// Class to get member ids and types out of a group. Split between common and version specifc code due to the change between Stu3 and R4 to the ResourceReference object.
    /// </summary>
    public partial class GroupMemberExtractor : IGroupMemberExtractor
    {
        private readonly IScoped<IFhirDataStore> _fhirDataStore;
        private readonly ResourceDeserializer _resourceDeserializer;
        private readonly Func<IScoped<ISearchService>> _searchServiceFactory;

        public GroupMemberExtractor(
            IScoped<IFhirDataStore> fhirDataStore,
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
            var groupResource = await _fhirDataStore.Value.GetAsync(new ResourceKey(KnownResourceTypes.Group, groupId), cancellationToken);

            var group = _resourceDeserializer.Deserialize(groupResource);
            var groupContents = group.ToPoco<Group>().Member;

            var members = new List<Tuple<string, string>>();

            foreach (Group.MemberComponent member in groupContents)
            {
                var id = member.Entity.Reference;
                var type = await GetResourceType(member, cancellationToken);
                members.Add(new Tuple<string, string>(id, type));
            }

            return members;
        }

        private async Task<string> GetResourceTypeFromDatabase(string id, CancellationToken cancellationToken)
        {
            using (IScoped<ISearchService> searchService = _searchServiceFactory())
            {
                var queryParametersList = new List<Tuple<string, string>>()
                    {
                        Tuple.Create(KnownQueryParameterNames.Id, id),
                    };

                var searchResult = await searchService.Value.SearchAsync(
                    resourceType: null,
                    queryParametersList,
                    cancellationToken);

                return searchResult.Results.First().Resource.ResourceTypeName;
            }
        }
    }
}
