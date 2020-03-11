// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Reflection;
using EnsureThat;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace Microsoft.Health.SqlServer.Api.Controllers
{
    public class SchemaControllerFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
    {
        private readonly Type _schemaVersionType;

        public SchemaControllerFeatureProvider(Type schemaVersionType)
        {
            EnsureArg.IsNotNull(schemaVersionType, nameof(schemaVersionType));

            _schemaVersionType = schemaVersionType;
        }

        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            var controllerType = typeof(SchemaController<>).MakeGenericType(_schemaVersionType).GetTypeInfo();

            feature.Controllers.Add(controllerType);
        }
    }
}
