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
    public sealed class ValidateCodeParametersFilter : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            bool typeParameterExists = context.ActionArguments.TryGetValue("typeParameter", out var typeParameter);
            context.ActionArguments.TryGetValue("code", out var code);
            context.ActionArguments.TryGetValue("system", out var system);
            context.ActionArguments.TryGetValue("parameters", out var parameters);

            if ((typeParameterExists &&
                !(string.Equals(typeParameter.ToString(), "CodeSystem", StringComparison.OrdinalIgnoreCase) || string.Equals(typeParameter.ToString(), "ValueSet", StringComparison.OrdinalIgnoreCase))) &&
                parameters == null)
            {
                throw new RequestNotValidException(Resources.ValidateCodeInvalidResourceType);
            }

            if ((string.IsNullOrEmpty((string)code) || string.IsNullOrEmpty((string)system)) && parameters == null)
            {
                throw new RequestNotValidException(Resources.ValidateCodeMissingSystemOrCode);
            }

            bool hasCoding = false;
            bool hasValueSetOrCodeSystem = false;

            if (parameters != null)
            {
                if (((Parameters)parameters).Parameter.Count != 2)
                {
                    throw new RequestNotValidException(Resources.ValidateCodeInvalidParemeters);
                }

                foreach (var paramComponent in ((Parameters)parameters).Parameter)
                {
                    if (string.Equals(paramComponent.Name, "valueSet", StringComparison.OrdinalIgnoreCase))
                    {
                        hasValueSetOrCodeSystem = true;
                    }
                    else if (string.Equals(paramComponent.Name, "codeSystem", StringComparison.OrdinalIgnoreCase))
                    {
                        hasValueSetOrCodeSystem = true;
                    }

                    if (string.Equals(paramComponent.Name, "coding", StringComparison.OrdinalIgnoreCase))
                    {
                        hasCoding = true;
                    }
                }

                if (!hasCoding)
                {
                    throw new RequestNotValidException(Resources.ParameterMissingCoding);
                }

                if (!hasValueSetOrCodeSystem)
                {
                    throw new RequestNotValidException(Resources.ValidateCodeInvalidResourceType);
                }
            }
        }
    }
}
