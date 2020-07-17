// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    public interface IGroupMemberExtractor
    {
        /// <summary>
        /// Queries the database for a Group resource and returns a list of the Group's members.
        /// </summary>
        /// <param name="groupId">The id of the Group resoruce</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of Tuples for the members of the Group. The tuples' first values are the resource ids and the second values are the resource type.</returns>
        public Task<List<Tuple<string, string>>> GetGroupMembers(string groupId, CancellationToken cancellationToken);
    }
}
