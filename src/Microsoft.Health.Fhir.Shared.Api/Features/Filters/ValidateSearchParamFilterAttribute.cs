// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Shared.Core.Features.Search.Parameters;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.Features.Filters
{
    [AttributeUsage(AttributeTargets.Method)]
    internal class ValidateSearchParamFilterAttribute : ActionFilterAttribute
    {
        private ISearchParameterValidator _searchParameterValidator;
        private ISearchParameterEditor _searchParameterEditor;

        public ValidateSearchParamFilterAttribute(ISearchParameterValidator searchParamValidator, ISearchParameterEditor searchParameterEditor)
        {
            EnsureArg.IsNotNull(searchParamValidator, nameof(searchParamValidator));
            EnsureArg.IsNotNull(searchParameterEditor, nameof(searchParameterEditor));

            _searchParameterValidator = searchParamValidator;
            _searchParameterEditor = searchParameterEditor;
        }

        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            if (context.ActionArguments.TryGetValue(KnownActionParameterNames.Resource, out var parsedModel))
            {
                if (parsedModel is SearchParameter)
                {
                    // wait for the validation checks to pass before allowing the FHIRController action to continue
                    await _searchParameterValidator.ValidateSearchParamterInput(
                        parsedModel as SearchParameter,
                        context.HttpContext.Request.Method,
                        context.HttpContext.RequestAborted);
                }
            }

            // wait for the Action to execute
            await next();

            if (HttpMethods.IsPost(context.HttpContext.Request.Method))
            {
                // Once the SearchParameter resource is committed to the data store, we can update the in
                // memory SearchParameterDefinitionManager, and persist the status to the data store
                await _searchParameterEditor.AddSearchParameterAsync(parsedModel as SearchParameter, context.HttpContext.RequestAborted);
            }
        }
    }
}
