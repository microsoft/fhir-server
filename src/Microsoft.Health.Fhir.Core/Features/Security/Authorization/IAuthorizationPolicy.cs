// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Security.Claims;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Security.Authorization
{
    public interface IAuthorizationPolicy
    {
        Task<bool> HasPermissionAsync(ClaimsPrincipal user, ResourceAction action);
    }
}
