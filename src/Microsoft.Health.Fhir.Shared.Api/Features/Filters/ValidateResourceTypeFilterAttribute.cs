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
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Api.Features.Filters
{
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class ValidateResourceTypeFilterAttribute : ParameterCompatibleFilter
    {
        public ValidateResourceTypeFilterAttribute(bool allowParametersResource = false)
            : base(allowParametersResource)
        {
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            if (context.RouteData.Values.TryGetValue(KnownActionParameterNames.ResourceType, out var actionModelType) &&
                context.ActionArguments.TryGetValue(KnownActionParameterNames.Resource, out var parsedModel))
            {
                string resourceTypeName = GetResourceTypeName(parsedModel);
                ValidateType(resourceTypeName, (string)actionModelType);
            }

            // Validated Resource Type of Parameters
            if (context.ActionArguments.TryGetValue(KnownActionParameterNames.ParamsResource, out var parsedParamModel))
            {
                string resourceTypeName = GetResourceTypeName(parsedParamModel);
                ValidateType(resourceTypeName, KnownResourceTypes.Parameters);
            }
        }

        private string GetResourceTypeName(object parsedModel)
        {
            // Handle IResourceElement (Ignixa types like IgnixaResourceElement)
            if (parsedModel is IResourceElement resourceElement)
            {
                return resourceElement.InstanceType;
            }

            // Handle Firely Resource types
            if (parsedModel is Resource resource)
            {
                var resolved = ParseResource(resource);
                return resolved.TypeName;
            }

            throw new InvalidOperationException($"Cannot extract resource type from {parsedModel?.GetType().Name ?? "null"}");
        }

        private static void ValidateType(string actualType, string expectedType)
        {
            if (!string.Equals(expectedType, actualType, StringComparison.OrdinalIgnoreCase))
            {
                throw new ResourceNotValidException(new List<ValidationFailure>
                    {
                        new ValidationFailure(nameof(Base.TypeName), Api.Resources.ResourceTypeMismatch),
                    });
            }
        }
    }
}
