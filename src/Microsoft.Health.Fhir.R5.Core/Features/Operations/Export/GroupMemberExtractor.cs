// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    /// <summary>
    /// Class to get member ids and types out of a group. Split between common and version specifc code due to the change between Stu3 and R4 to the ResourceReference object.
    /// </summary>
    public partial class GroupMemberExtractor : IGroupMemberExtractor
    {
        private async Task<string> GetResourceType(Group.MemberComponent member, CancellationToken cancellationToken)
        {
            var id = member.Entity.Reference;
            var type = member.Entity.Type;
            if (string.IsNullOrEmpty(type))
            {
                type = await GetResourceTypeFromDatabase(id, cancellationToken);
            }

            return type;
        }
    }
}
