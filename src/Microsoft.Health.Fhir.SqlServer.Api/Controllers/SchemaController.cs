// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.SqlServer.Api.Features.Filters;
using Microsoft.Health.Fhir.SqlServer.Api.Features.Routing;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;

namespace Microsoft.Health.Fhir.SqlServer.Api.Controllers
{
    [NotImplementedExceptionFilter]
    [Route(KnownRoutes.SchemaRoot)]
    public class SchemaController : Controller
    {
        private readonly SchemaInformation _schemaInformation;
        private readonly IUrlResolver _urlResolver;
        private readonly ILogger<SchemaController> _logger;

        public SchemaController(SchemaInformation schemaInformation, IUrlResolver urlResolver, ILogger<SchemaController> logger)
        {
            EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
            EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _schemaInformation = schemaInformation;
            _urlResolver = urlResolver;
            _logger = logger;
        }

        [HttpGet]
        [Route(KnownRoutes.Versions)]
        public ActionResult AvailableVersions()
        {
            _logger.LogInformation("Attempting to get available schemas");

            var availableSchemas = new List<object>();
            var currentVersion = _schemaInformation.Current ?? 0;
            foreach (var version in Enum.GetValues(typeof(SchemaVersion)).Cast<SchemaVersion>().Where(sv => sv >= currentVersion))
            {
                var routeValues = new Dictionary<string, object> { { "id", (int)version } };
                Uri scriptUri = _urlResolver.ResolveRouteNameUrl(RouteNames.Script, routeValues);
                availableSchemas.Add(new { id = version, script = scriptUri });
            }

            return new JsonResult(availableSchemas);
        }

        [HttpGet]
        [Route(KnownRoutes.Current)]
        public ActionResult CurrentVersion()
        {
            _logger.LogInformation("Attempting to get current schemas");

            throw new NotImplementedException(Resources.CurrentVersionNotImplemented);
        }

        [HttpGet]
        [Route(KnownRoutes.Script, Name = RouteNames.Script)]
        public ActionResult SqlScript(int id)
        {
            _logger.LogInformation($"Attempting to get script for schema version: {id}");

            throw new NotImplementedException(Resources.ScriptNotImplemented);
        }

        [HttpGet]
        [Route(KnownRoutes.Compatibility)]
        public ActionResult Compatibility()
        {
            _logger.LogInformation("Attempting to get compatibility");

            throw new NotImplementedException(Resources.CompatibilityNotImplemented);
        }
    }
}
