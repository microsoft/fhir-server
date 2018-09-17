// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Core.Features.Security
{
    public interface ISecurityRepository
    {
        Task<Role> GetRoleAsync(string name, CancellationToken cancellationToken);

        Task<IEnumerable<Role>> GetAllRolesAsync(CancellationToken cancellationToken);

        Task<Role> UpsertRoleAsync(Role role, WeakETag weakETag, CancellationToken cancellationToken);

        Task DeleteRoleAsync(string name, CancellationToken cancellationToken);
    }
}
