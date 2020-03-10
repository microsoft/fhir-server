// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EnsureThat;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Health.SqlServer.Features.Schema;

namespace Microsoft.Health.SqlServer.Api.Controllers
{
    public class SchemaControllerFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
    {
        private readonly Type _schemaVersionType;

        public SchemaControllerFeatureProvider(ISchemaInformation schemaInformation)
        {
            EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));

            _schemaVersionType = schemaInformation.SchemaVersionEnumType;
        }

        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            var controllerType = typeof(SchemaController<>).MakeGenericType(_schemaVersionType).GetTypeInfo();

            feature.Controllers.Add(controllerType);
        }
    }
}
