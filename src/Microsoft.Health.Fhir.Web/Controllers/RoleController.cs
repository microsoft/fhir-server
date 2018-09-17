// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Web.Features.Filters;
using Microsoft.Net.Http.Headers;

namespace Microsoft.Health.Fhir.Web.Controllers
{
    [ServiceFilter(typeof(SecurityControllerFeatureFilterAttribute))]
    [Route("Security/Role/")]
    public class RoleController : Controller
    {
        private readonly ISecurityRepository _securityRepository;

        public RoleController(ISecurityRepository securityRepository)
        {
            EnsureArg.IsNotNull(securityRepository, nameof(securityRepository));

            _securityRepository = securityRepository;
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            return Ok(await _securityRepository.GetAllRolesAsync(cancellationToken));
        }

        [HttpGet]
        [Route("{roleName}")]
        [AllowAnonymous]
        public async Task<IActionResult> Index(string roleName, CancellationToken cancellationToken)
        {
            return Ok(await _securityRepository.GetRoleAsync(roleName, cancellationToken));
        }

        [HttpPut]
        [Route("{roleName}")]
        [AllowAnonymous]
        public async Task<IActionResult> Update(string roleName, [FromBody] Role role, CancellationToken cancellationToken)
        {
            if (roleName != role.Name)
            {
                return Conflict();
            }

            var suppliedWeakETag = HttpContext.Request.Headers[HeaderNames.IfMatch];

            WeakETag weakETag = null;
            if (!string.IsNullOrWhiteSpace(suppliedWeakETag))
            {
                weakETag = WeakETag.FromWeakETag(suppliedWeakETag);
            }

            return Ok(await _securityRepository.UpsertRoleAsync(role, weakETag, cancellationToken));
        }

        [HttpDelete]
        [Route("{roleName}")]
        [AllowAnonymous]
        public async Task Delete(string roleName, CancellationToken cancellationToken)
        {
            await _securityRepository.DeleteRoleAsync(roleName, cancellationToken);
        }
    }
}
