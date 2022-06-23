﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Api.Features.Filters
{
    /// <summary>
    /// Validate that the deserialized request body object is of type Hl7.Fhir.Model.Parameters.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class ValidateParametersResourceAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            context.ActionArguments.TryGetValue("inputParams", out var inputResource);
            if (inputResource is not Parameters)
            {
                throw new BadRequestException(string.Format(
                    "Expected input object to be of type 'Hl7.Fhir.Model.Parameters' instead of '{0}'",
                    inputResource.GetType().FullName));
            }
        }
    }
}
