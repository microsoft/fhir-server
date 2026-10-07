// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using EnsureThat;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes;

namespace Microsoft.Health.Fhir.Api.Features.Filters
{
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class ValidateIdSegmentAttribute : ParameterCompatibleFilter
    {
        public ValidateIdSegmentAttribute(bool allowParametersResource = false)
            : base(allowParametersResource)
        {
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            if (context.RouteData.Values.TryGetValue(KnownActionParameterNames.Id, out var resourceId)
                && string.IsNullOrWhiteSpace((string)resourceId))
            {
                throw new ResourceNotValidException(new List<ValidationFailure>
                {
                    new ValidationFailure("ResourceKey.Id", string.Format(CultureInfo.InvariantCulture, Core.Resources.IdRequirements, (context.HttpContext?.RequestServices?.GetService<ResourceIdPolicy>() ?? ResourceIdPolicy.Standard).MaxLength)),
                });
            }
        }
    }
}
