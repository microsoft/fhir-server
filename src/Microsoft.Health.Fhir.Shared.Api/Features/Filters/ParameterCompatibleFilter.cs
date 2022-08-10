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
        protected ParameterCompatibleFilter(bool allowParametersResource)
        {
            AllowParametersResource = allowParametersResource;
        }

        protected bool AllowParametersResource { get; }

        protected Resource ParseResource(Resource resource)
        {
            if (AllowParametersResource && resource.TypeName == KnownResourceTypes.Parameters)
            {
                if (((Parameters)resource).Parameter.Find(param => param.Name.Equals("resource", StringComparison.OrdinalIgnoreCase)) == null)
                {
                    return null;
                }

                resource = ((Parameters)resource).Parameter.Find(param => param.Name.Equals("resource", StringComparison.OrdinalIgnoreCase)).Resource;
            }

            return resource;
        }
    }
}
