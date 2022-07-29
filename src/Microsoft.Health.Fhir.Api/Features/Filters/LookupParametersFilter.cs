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
    public sealed class LookupParametersFilter : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            bool typeParameterExists = context.ActionArguments.TryGetValue("typeParameter", out var typeParameter);
            context.ActionArguments.TryGetValue("code", out var code);
            context.ActionArguments.TryGetValue("system", out var system);
            context.ActionArguments.TryGetValue("parameters", out var parameters);

            if ((string.IsNullOrEmpty((string)code) || string.IsNullOrEmpty((string)system)) && parameters == null)
            {
                throw new RequestNotValidException(Resources.LookupInvalidMissingSystemOrCode);
            }

            bool hasCoding = false;

            if (parameters != null)
            {
                foreach (var paramComponent in ((Parameters)parameters).Parameter)
                {
                    if (string.Equals(paramComponent.Name, "coding", StringComparison.OrdinalIgnoreCase))
                    {
                        hasCoding = true;
                    }
                }

                if (!hasCoding)
                {
                    throw new RequestNotValidException(Resources.LookupMissingCodingComponent);
                }
            }
        }
    }
}
