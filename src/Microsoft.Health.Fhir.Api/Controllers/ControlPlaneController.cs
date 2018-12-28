// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Health.ControlPlane.Core.Features.Persistence;
using Microsoft.Health.ControlPlane.Core.Features.Rbac;
using Microsoft.Net.Http.Headers;

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

        [HttpPut]
        [AllowAnonymous]
        public async Task<IActionResult> Update([FromBody] IdentityProvider identityProvider)
        {
            var eTag = HttpContext.Request.Headers[HeaderNames.IfMatch];

            UpsertResponse<IdentityProvider> response = await _rbacService.UpsertIdentityProviderAsync(identityProvider, eTag, HttpContext.RequestAborted);

            SetETagHeader(response.ETag);

            switch (response.OutcomeType)
            {
                case UpsertOutcomeType.Created:
                    return Created(new Uri($"{Request.Scheme}://{Request.Host}{Request.Path}/{identityProvider.Name}"), response.ControlPlaneResource);
                case UpsertOutcomeType.Updated:
                    return Ok(response.ControlPlaneResource);
            }

            return BadRequest(response.ControlPlaneResource);
        }

        [HttpDelete]
        [Route("{IdentityProviderName}")]
        [AllowAnonymous]
        public async Task<IActionResult> Delete(string identityProviderName, CancellationToken cancellationToken)
        {
            var eTag = HttpContext.Request.Headers[HeaderNames.IfMatch];
            await _rbacService.DeleteIdentityProviderAsync(identityProviderName, eTag, cancellationToken);
            return Ok();
        }

        private void SetETagHeader(string eTag)
        {
            Response.Headers.Add(HeaderNames.ETag, eTag);
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            var response = await _rbacService.GetAllIdentityProvidersAsync(cancellationToken);
            return Ok(response);
        }
    }
}
