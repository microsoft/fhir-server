// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net.Http;
using AngleSharp.Text;
using EnsureThat;
using FluentValidation.Results;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Validation;

namespace Microsoft.Health.Fhir.Api.Features.Filters
{
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class ValidateResourceTypeFilterAttribute : ParameterCompatibleFilter
    {
        private static readonly string[] _httpMethodsRequiringValidResources = new string[]
        {
            HttpMethod.Patch.ToString(),
        };

        public ValidateResourceTypeFilterAttribute(bool allowParametersResource = false)
            : base(allowParametersResource)
        {
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            object actionModelType = null;
            object parsedModel = null;
            string httpMethod = context.HttpContext?.Request?.Method;

            if (context.RouteData.Values.TryGetValue(KnownActionParameterNames.ResourceType, out actionModelType) &&
                context.ActionArguments.TryGetValue(KnownActionParameterNames.Resource, out parsedModel))
            {
                var resource = ParseResource((Resource)parsedModel);
                ValidateType(resource, (string)actionModelType);
            }
            else if (_httpMethodsRequiringValidResources.Contains(httpMethod, StringComparison.OrdinalIgnoreCase) && actionModelType != null && parsedModel == null)
            {
                // In this case, the resource type requires a valid resource as part of the request.
                ValidateType(parsedModel as Resource, actionModelType as string);
            }
        }

        private static void ValidateType(Resource resource, string expectedType)
        {
            if (!string.Equals(expectedType, resource?.TypeName, StringComparison.OrdinalIgnoreCase))
            {
                throw new ResourceNotValidException(new List<ValidationFailure>
                    {
                        new ValidationFailure(nameof(Base.TypeName), Api.Resources.ResourceTypeMismatch),
                    });
            }
        }
    }
}
