// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using FluentValidation.Results;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Namotion.Reflection;

namespace Microsoft.Health.Fhir.Api.Features.Filters
{
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class ValidateResourceIdFilterAttribute : ParameterCompatibleFilter
    {
        public ValidateResourceIdFilterAttribute(bool allowParametersResource = false)
            : base(allowParametersResource)
        {
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            bool foundActionId = context.RouteData.Values.TryGetValue(KnownActionParameterNames.Id, out var actionId);

            if (!foundActionId)
            {
                actionId = GetActionIdFromRoutingFeature(context.HttpContext, KnownActionParameterNames.Id);
            }

            if (actionId != null &&
                context.ActionArguments.TryGetValue(KnownActionParameterNames.Resource, out var parsedModel))
            {
                var resource = ParseResource((Resource)parsedModel);
                ValidateId(resource, (string)actionId);
            }
            else
            {
                throw new ResourceNotValidException(new List<ValidationFailure>
                {
                    new ValidationFailure(nameof(Base.TypeName), Api.Resources.ResourceAndIdRequired),
                });
            }
        }

        private static void ValidateId(Resource resource, string expectedId)
        {
            var location = $"{resource.TypeName}.id";
            if (string.IsNullOrWhiteSpace(resource.Id))
            {
                throw new ResourceNotValidException(new List<ValidationFailure>
                    {
                        new ValidationFailure(location, Api.Resources.ResourceIdRequired),
                    });
            }

            if (!string.Equals(expectedId, resource.Id, StringComparison.Ordinal))
            {
                throw new ResourceNotValidException(new List<ValidationFailure>
                    {
                        new ValidationFailure(location, Api.Resources.UrlResourceIdMismatch),
                    });
            }
        }

        private static string GetActionIdFromRoutingFeature(HttpContext context, string id)
        {
            if (context.Features[typeof(IRoutingFeature)] is IRoutingFeature routingFeature)
            {
                var routeValues = routingFeature.RouteData.Values;

                routeValues.TryGetValue(id, out var actionId);

                return actionId?.ToString();
            }

            return null;
        }
    }
}
