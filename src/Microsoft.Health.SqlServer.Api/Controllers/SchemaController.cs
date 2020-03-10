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
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Health.SqlServer.Api.Features.Filters;
using Microsoft.Health.SqlServer.Api.Features.Routing;
using Microsoft.Health.SqlServer.Features.Schema;

namespace Microsoft.Health.SqlServer.Api.Controllers
{
    [HttpExceptionFilter]
    [Route(KnownRoutes.SchemaRoot)]
    public class SchemaController<TSchemaVersionEnum> : Controller
        where TSchemaVersionEnum : Enum
    {
        private readonly ISchemaInformation _schemaInformation;
        private readonly IUrlHelper _urlHelper;
        private readonly ILogger<SchemaController<TSchemaVersionEnum>> _logger;

        public SchemaController(ISchemaInformation schemaInformation, IUrlHelperFactory urlHelperFactory, IActionContextAccessor actionContextAccessor, ILogger<SchemaController<TSchemaVersionEnum>> logger)
        {
            EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
            EnsureArg.IsNotNull(urlHelperFactory, nameof(urlHelperFactory));
            EnsureArg.IsNotNull(actionContextAccessor, nameof(actionContextAccessor));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _schemaInformation = schemaInformation;
            _urlHelper = urlHelperFactory.GetUrlHelper(actionContextAccessor.ActionContext);
            _logger = logger;
        }

        [HttpGet]
        [AllowAnonymous]
        [Route(KnownRoutes.Versions)]
        public ActionResult AvailableVersions()
        {
            _logger.LogInformation("Attempting to get available schemas");

            var availableSchemas = new List<object>();
            var currentVersion = _schemaInformation.Current ?? 0;
            foreach (var version in Enum.GetValues(typeof(TSchemaVersionEnum)).Cast<int>().Where(sv => sv >= currentVersion))
            {
                var routeValues = new Dictionary<string, object> { { "id", version } };
                string scriptUri = _urlHelper.RouteUrl(RouteNames.Script, routeValues);
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
            return File(ScriptProvider.GetMigrationScriptAsBytes<TSchemaVersionEnum>(id), "application/sql", fileName);
        }

        [HttpGet]
        [AllowAnonymous]
        [Route(KnownRoutes.Compatibility)]
        public ActionResult Compatibility()
        {
            _logger.LogInformation("Attempting to get compatibility");

            throw new NotImplementedException(Resources.CompatibilityNotImplemented);
        }
    }
}
