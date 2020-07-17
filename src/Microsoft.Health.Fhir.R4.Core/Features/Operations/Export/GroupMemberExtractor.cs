// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    public class GroupMemberExtractor : IGroupMemberExtractor
    {
        private readonly IFhirDataStore _fhirDataStore;
        private readonly ResourceDeserializer _resourceDeserializer;

        public GroupMemberExtractor(
            IFhirDataStore fhirDataStore,
            ResourceDeserializer resourceDeserializer)
        {
            EnsureArg.IsNotNull(fhirDataStore, nameof(fhirDataStore));
            EnsureArg.IsNotNull(resourceDeserializer, nameof(resourceDeserializer));

            _fhirDataStore = fhirDataStore;
            _resourceDeserializer = resourceDeserializer;
        }

        public async Task<List<Tuple<string, string>>> GetGroupMembers(string groupId, CancellationToken cancellationToken)
        {
            var groupResource = await _fhirDataStore.GetAsync(new ResourceKey(KnownResourceTypes.Group, groupId), cancellationToken);

            var group = _resourceDeserializer.Deserialize(groupResource);
            var groupContents = group.ToPoco<Group>().Member;

            return groupContents.ConvertAll(member => new Tuple<string, string>(member.Entity.Reference, member.Entity.Type));
        }
    }
}
