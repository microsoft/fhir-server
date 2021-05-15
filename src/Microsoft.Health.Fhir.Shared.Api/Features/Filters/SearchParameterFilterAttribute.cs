// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Shared.Core.Features.Search.Parameters;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.Features.Filters
{
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class SearchParameterFilterAttribute : ActionFilterAttribute
    {
        private ISearchParameterValidator _searchParameterValidator;

        public SearchParameterFilterAttribute(ISearchParameterValidator searchParamValidator)
        {
            EnsureArg.IsNotNull(searchParamValidator, nameof(searchParamValidator));

            _searchParameterValidator = searchParamValidator;
        }

        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            if (context.ActionArguments.TryGetValue(KnownActionParameterNames.Resource, out var parsedModel))
            {
                if (parsedModel is SearchParameter searchParameter)
                {
                    // wait for the validation checks to pass before allowing the FHIRController action to continue
                    await _searchParameterValidator.ValidateSearchParameterInput(
                        searchParameter,
                        context.HttpContext.Request.Method,
                        context.HttpContext.RequestAborted);
                }
            }

            await next();
        }
    }
}
