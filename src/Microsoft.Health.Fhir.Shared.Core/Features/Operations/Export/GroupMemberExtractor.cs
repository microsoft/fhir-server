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
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    /// <summary>
    /// Class to get member ids and types out of a group.
    /// </summary>
    public class GroupMemberExtractor : IGroupMemberExtractor
    {
        private readonly Func<IScoped<IFhirDataStore>> _fhirDataStoreFactory;
        private readonly ResourceDeserializer _resourceDeserializer;
        private readonly IReferenceToElementResolver _referenceToElementResolver;

        public GroupMemberExtractor(
            Func<IScoped<IFhirDataStore>> fhirDataStoreFactory,
            ResourceDeserializer resourceDeserializer,
            IReferenceToElementResolver referenceToElementResolver)
        {
            EnsureArg.IsNotNull(fhirDataStoreFactory, nameof(fhirDataStoreFactory));
            EnsureArg.IsNotNull(resourceDeserializer, nameof(resourceDeserializer));
            EnsureArg.IsNotNull(referenceToElementResolver, nameof(referenceToElementResolver));

            _fhirDataStoreFactory = fhirDataStoreFactory;
            _resourceDeserializer = resourceDeserializer;
            _referenceToElementResolver = referenceToElementResolver;
        }

        public async Task<HashSet<string>> GetGroupPatientIds(string groupId, DateTimeOffset groupMembershipTime, CancellationToken cancellationToken)
        {
            return await GetGroupPatientIdsHelper(groupId, groupMembershipTime, null, cancellationToken);
        }

        private async Task<HashSet<string>> GetGroupPatientIdsHelper(string groupId, DateTimeOffset groupMembershipTime, HashSet<string> groupsAlreadyChecked, CancellationToken cancellationToken)
        {
            if (groupsAlreadyChecked == null)
            {
                groupsAlreadyChecked = new HashSet<string>();
            }

            groupsAlreadyChecked.Add(groupId);

            var groupContents = await GetGroupMembers(groupId, groupMembershipTime, cancellationToken);

            var patientIds = new HashSet<string>();

            foreach (Tuple<string, string> entity in groupContents)
            {
                var resourceId = entity.Item1;
                var resourceType = entity.Item2;

                // Only Patient resources and their compartment resources are exported as part of a Group export. All other resource types are ignored.
                // Nested Group resources are checked to see if they contain other Patients.
                switch (resourceType)
                {
                    case KnownResourceTypes.Patient:
                        patientIds.Add(resourceId);
                        break;
                    case KnownResourceTypes.Group:
                        // need to check that loops aren't happening
                        if (!groupsAlreadyChecked.Contains(resourceId))
                        {
                            patientIds.UnionWith(await GetGroupPatientIdsHelper(resourceId, groupMembershipTime, groupsAlreadyChecked, cancellationToken));
                        }

                        break;
                }
            }

            return patientIds;
        }

        public async Task<List<Tuple<string, string>>> GetGroupMembers(string groupId, DateTimeOffset groupMembershipTime, CancellationToken cancellationToken)
        {
            ResourceWrapper groupResource;
            using (IScoped<IFhirDataStore> dataStore = _fhirDataStoreFactory.Invoke())
            {
                groupResource = await dataStore.Value.GetAsync(new ResourceKey(KnownResourceTypes.Group, groupId), cancellationToken);
            }

            if (groupResource == null)
            {
                throw new ResourceNotFoundException($"Group {groupId} was not found.", true);
            }

            var group = _resourceDeserializer.Deserialize(groupResource);
            var groupContents = group.ToPoco<Group>().Member;

            var members = new List<Tuple<string, string>>();

            foreach (Group.MemberComponent member in groupContents)
            {
                var fhirGroupMembershipTime = new FhirDateTime(groupMembershipTime);
                if (
                    (member.Inactive == null
                    || member.Inactive == false)
                    && (member.Period?.EndElement == null
                    || member.Period?.EndElement > fhirGroupMembershipTime)
                    && (member.Period?.StartElement == null
                    || member.Period?.StartElement < fhirGroupMembershipTime))
                {
                    var element = _referenceToElementResolver.Resolve(member.Entity.Reference);
                    string id = (string)element.Children("id").First().Value;
                    string resourceType = element.InstanceType;

                    members.Add(Tuple.Create(id, resourceType));
                }
            }

            return members;
        }
    }
}
