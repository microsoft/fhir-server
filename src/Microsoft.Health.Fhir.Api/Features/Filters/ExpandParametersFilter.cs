// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Health.Fhir.Core.Exceptions;

namespace Microsoft.Health.Fhir.Api.Features.Filters
{
    /// <summary>
    /// Validate that the deserialized request body object is of type Hl7.Fhir.Model.Parameters.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class ExpandParametersFilter : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            context.ActionArguments.TryGetValue("parameters", out var parameters);
            context.ActionArguments.TryGetValue("idParameter", out var idParameter);
            context.ActionArguments.TryGetValue("url", out var url);

            bool hasValueSet = false;

            if (parameters != null)
            {
                foreach (var paramComponent in ((Parameters)parameters).Parameter)
                {
                    if (string.Equals(paramComponent.Name, "valueSet", StringComparison.OrdinalIgnoreCase))
                    {
                        hasValueSet = true;
                        break;
                    }
                }

                if (!hasValueSet)
                {
                    throw new RequestNotValidException(Resources.ExpandMissingValueSetParameterComponent);
                }
            }

            if (!((idParameter != null) ^ (url != null)) && parameters == null)
            {
                throw new RequestNotValidException(Resources.ExpandInvalidIdParamterXORUrl);
            }
        }
    }
}
