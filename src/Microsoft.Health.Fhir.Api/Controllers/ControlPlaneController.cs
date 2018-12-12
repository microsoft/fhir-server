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

namespace Microsoft.Health.Fhir.Api.Controllers
{
    // TODO: Remove before PR
    [Route("Admin/IdentityProvider/")]
    public class ControlPlaneController : Controller
    {
        private readonly IRbacService _rbacService;

        public ControlPlaneController(IRbacService rbacService)
        {
            EnsureArg.IsNotNull(rbacService, nameof(rbacService));

            _rbacService = rbacService;
        }

        [HttpGet]
        [Route("{IdentityProviderName}")]
        [AllowAnonymous]
        public async Task<IActionResult> Index(string identityProviderName, CancellationToken cancellationToken)
        {
            var response = await _rbacService.GetIdentityProviderAsync(identityProviderName, cancellationToken);
            return Ok(response);
        }
    }
}
