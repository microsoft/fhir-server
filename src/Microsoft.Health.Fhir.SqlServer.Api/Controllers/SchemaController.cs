// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Api.Features.Filters;
using Microsoft.Health.Fhir.SqlServer.Api.Features.Routing;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;

namespace Microsoft.Health.Fhir.SqlServer.Api.Controllers
{
    [HttpExceptionFilter]
    [Route(KnownRoutes.SchemaRoot)]
    public class SchemaController : Controller
    {
        private readonly SchemaInformation _schemaInformation;
        private readonly IUrlResolver _urlResolver;
        private readonly ILogger<SchemaController> _logger;
        private readonly Func<IScoped<IQueryProcessor>> _queryProcessor;

        public SchemaController(SchemaInformation schemaInformation, IUrlResolver urlResolver, ILogger<SchemaController> logger, Func<IScoped<IQueryProcessor>> queryProcessor)
        {
            EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
            EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(queryProcessor, nameof(queryProcessor));

            _schemaInformation = schemaInformation;
            _urlResolver = urlResolver;
            _logger = logger;
            _queryProcessor = queryProcessor;
        }

        [HttpGet]
        [AllowAnonymous]
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
        [AllowAnonymous]
        [Route(KnownRoutes.Current)]
        public ActionResult CurrentVersion()
        {
            _logger.LogInformation("Attempting to get current schemas");

            throw new NotImplementedException(Resources.CurrentVersionNotImplemented);
        }

        [HttpGet]
        [AllowAnonymous]
        [Route(KnownRoutes.Script, Name = RouteNames.Script)]
        public FileContentResult SqlScript(int id)
        {
            _logger.LogInformation($"Attempting to get script for schema version: {id}");
            string fileName = $"{id}.sql";
            return File(ScriptProvider.GetMigrationScriptAsBytes(id), "application/json", fileName);
        }

        [HttpGet]
        [AllowAnonymous]
        [Route(KnownRoutes.Compatibility)]
        public ActionResult Compatibility()
        {
            _logger.LogInformation("Attempting to get compatibility");

            var minVersion = (int)_schemaInformation.MinimumSupportedVersion;
            var compatibilityVersions = new Dictionary<string, object> { { "min", minVersion } };
            using (IScoped<IQueryProcessor> query = _queryProcessor())
            {
                var maxVersion = query.Value.GetLatestCompatibleVersion((int)_schemaInformation.MaximumSupportedVersion);
                compatibilityVersions.Add("max", maxVersion);
            }

            return new JsonResult(compatibilityVersions);
        }
    }
}
