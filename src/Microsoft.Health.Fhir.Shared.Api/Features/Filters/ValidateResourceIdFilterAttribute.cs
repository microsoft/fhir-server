// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using FluentValidation.Results;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Api.Helpers;
using Microsoft.Health.Fhir.Core.Features.Validation;

namespace Microsoft.Health.Fhir.Api.Features.Filters
{
    [AttributeUsage(AttributeTargets.Method)]
    internal class ValidateResourceIdFilterAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            if (context.RouteData.Values.TryGetValue(KnownActionParameterNames.Id, out var actionId) &&
                context.ActionArguments.TryGetValue(KnownActionParameterNames.Resource, out var parsedModel))
            {
                var resource = (Resource)parsedModel;
                ValidationHelpers.ValidateId(resource, (string)actionId);
            }
            else
            {
                throw new ResourceNotValidException(new List<ValidationFailure>
                {
                    new ValidationFailure(nameof(Base.TypeName), Api.Resources.ResourceAndIdRequired),
                });
            }
        }
    }
}
