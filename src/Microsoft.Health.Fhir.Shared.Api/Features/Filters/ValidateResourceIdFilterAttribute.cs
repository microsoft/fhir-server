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
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Models;

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
                var (typeName, resourceId) = GetResourceInfo(parsedModel);
                ValidateId(typeName, resourceId, (string)actionId);
            }
            else
            {
                throw new ResourceNotValidException(new List<ValidationFailure>
                {
                    new ValidationFailure(nameof(Base.TypeName), Api.Resources.ResourceAndIdRequired),
                });
            }
        }

        private (string TypeName, string Id) GetResourceInfo(object parsedModel)
        {
            // Handle IResourceElement (Ignixa types like IgnixaResourceElement)
            if (parsedModel is IResourceElement resourceElement)
            {
                return (resourceElement.InstanceType, resourceElement.Id);
            }

            // Handle Firely Resource types
            if (parsedModel is Resource resource)
            {
                var resolved = ParseResource(resource);
                return (resolved.TypeName, resolved.Id);
            }

            throw new InvalidOperationException($"Cannot extract resource info from {parsedModel?.GetType().Name ?? "null"}");
        }

        private static void ValidateId(string typeName, string resourceId, string expectedId)
        {
            var location = $"{typeName}.id";
            if (string.IsNullOrWhiteSpace(resourceId))
            {
                throw new ResourceNotValidException(new List<ValidationFailure>
                    {
                        new ValidationFailure(location, Api.Resources.ResourceIdRequired),
                    });
            }

            if (!string.Equals(expectedId, resourceId, StringComparison.Ordinal))
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
