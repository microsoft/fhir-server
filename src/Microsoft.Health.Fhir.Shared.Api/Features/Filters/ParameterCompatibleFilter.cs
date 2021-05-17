// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Api.Features.Filters
{
    internal abstract class ParameterCompatibleFilter : ActionFilterAttribute
    {
        private bool _allowParametersResource;

        protected ParameterCompatibleFilter(bool allowParametersResource)
        {
            _allowParametersResource = allowParametersResource;
        }

        protected Resource ParseResource(Resource resource)
        {
            if (_allowParametersResource && resource.TypeName == KnownResourceTypes.Parameters)
            {
                resource = ((Parameters)resource).Parameter.Find(param => param.Name.Equals("resource", StringComparison.OrdinalIgnoreCase)).Resource;
            }

            return resource;
        }
    }
}
