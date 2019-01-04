// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Health.ControlPlane.Core.Features.Rbac;
using Microsoft.Health.ControlPlane.Core.Features.Rbac.Roles;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    public class ControlPlaneController : Controller
    {
        private readonly IRbacService _rbacService;

        public ControlPlaneController(IRbacService rbacService)
        {
            EnsureArg.IsNotNull(rbacService, nameof(rbacService));

            _rbacService = rbacService;
        }

        [HttpGet]
        [Route("Admin/IdentityProvider/{IdentityProviderName}")]
        [AllowAnonymous]
        public async Task<IActionResult> Index(string identityProviderName, CancellationToken cancellationToken)
        {
            var response = await _rbacService.GetIdentityProviderAsync(identityProviderName, cancellationToken);
            return Ok(response);
        }

        [HttpGet]
        [Route("Admin/Roles/{RoleName}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetRole(string roleName, CancellationToken cancellationToken)
        {
            var response = await _rbacService.GetRoleAsync(roleName, cancellationToken);
            return Ok(response);
        }

        [HttpGet]
        [Route("Admin/Roles")]
        [AllowAnonymous]
        public async Task<IActionResult> GetRoles(CancellationToken cancellationToken)
        {
            var response = await _rbacService.GetRoleForAllAsync(cancellationToken);
            return Ok(response);
        }

        [HttpPut]
        [Route("Admin/Roles")]
        [AllowAnonymous]
        public async Task<IActionResult> UpdateRole([FromBody] Role role, CancellationToken cancellationToken)
        {
            var response = await _rbacService.UpsertRoleAsync(role, cancellationToken);
            return Ok(response);
        }

        [HttpPost]
        [Route("Admin/Roles")]
        [AllowAnonymous]
        public async Task<IActionResult> CreateRole([FromBody] Role role, CancellationToken cancellationToken)
        {
            var response = await _rbacService.UpsertRoleAsync(role, cancellationToken);
            return Ok(response);
        }

        [HttpDelete]
        [Route("Admin/Roles/{RoleName}")]
        [AllowAnonymous]
        public async Task<IActionResult> DeleteRole(string roleName, CancellationToken cancellationToken)
        {
            var response = await _rbacService.DeleteRoleAsync(roleName, cancellationToken);
            return Ok(response);
        }
    }
}
