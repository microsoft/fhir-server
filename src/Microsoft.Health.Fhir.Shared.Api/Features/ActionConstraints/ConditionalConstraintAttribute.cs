// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Core.Features;

namespace Microsoft.Health.Fhir.Api.Features.ActionConstraints
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ConditionalConstraintAttribute : Attribute, IActionConstraint
    {
        public int Order => 0;

        public bool Accept(ActionConstraintContext context)
        {
            StringValues conditionalCreateHeader = context.RouteContext.HttpContext.Request.Headers[KnownHeaders.IfNoneExist];

            if (!string.IsNullOrEmpty(conditionalCreateHeader))
            {
                return true;
            }

            return false;
        }
    }
}
