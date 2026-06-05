// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Shared.Core.Features.Search.Parameters;
using Newtonsoft.Json.Linq;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.Features.Filters
{
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class SearchParameterFilterAttribute : ActionFilterAttribute
    {
        private readonly ISearchParameterValidator _searchParameterValidator;
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;

        public SearchParameterFilterAttribute(ISearchParameterValidator searchParamValidator, RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor)
        {
            EnsureArg.IsNotNull(searchParamValidator, nameof(searchParamValidator));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));

            _searchParameterValidator = searchParamValidator;
            _fhirRequestContextAccessor = fhirRequestContextAccessor;
        }

        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            var searchParameter = ExtractSearchParameter(context);
            if (searchParameter != null)
            {
                var fhirRequestContext = _fhirRequestContextAccessor.RequestContext;

                // Validate and capture/update LastUpdated with retry policy
                var lastUpdated = await SearchParameterRetryPolicyFactory.ExecuteAsync(
                    _fhirRequestContextAccessor,
                    async () => await _searchParameterValidator.ValidateSearchParameterInput(
                        searchParameter,
                        context.HttpContext.Request.Method,
                        context.HttpContext.RequestAborted,
                        fhirRequestContext.GetSearchParameterLastUpdated()));

                // Store the LastUpdated timestamp in context for use during the action
                fhirRequestContext.SetSearchParameterLastUpdated(lastUpdated);

                try
                {
                    // Execute the controller action
                    await next();
                }
                finally
                {
                    // Clear the LastUpdated after the operation completes to ensure each SearchParameter in a sequential bundle gets a fresh validation
                    fhirRequestContext.ClearSearchParameterLastUpdated();
                }
            }
            else
            {
                await next();
            }
        }

        private static SearchParameter ExtractSearchParameter(ActionExecutingContext context)
        {
            // Check for direct SearchParameter resource
            if (context.ActionArguments.TryGetValue(KnownActionParameterNames.Resource, out var parsedModel) &&
                parsedModel is SearchParameter searchParameter)
            {
                return searchParameter;
            }

            // Check for delete scenario with SearchParameter resource type
            if (context.ActionArguments.TryGetValue(KnownActionParameterNames.ResourceType, out var resourceType) &&
                string.Equals(resourceType?.ToString(), KnownResourceTypes.SearchParameter, StringComparison.OrdinalIgnoreCase))
            {
                return new SearchParameter();
            }

            return null;
        }
    }
}
