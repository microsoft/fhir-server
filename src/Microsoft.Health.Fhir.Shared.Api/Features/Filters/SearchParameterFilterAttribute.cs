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
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Shared.Core.Features.Search.Parameters;
using Newtonsoft.Json.Linq;
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

            var searchParameter = ExtractSearchParameter(context);
            if (searchParameter != null)
            {
                // Check if we're in a parallel bundle processing context
                var isParallel = false;

                if (context.HttpContext?.Request?.Headers != null
                    && context.HttpContext.Request.Headers.TryGetValue(BundleOrchestratorNamingConventions.HttpBundleInnerRequestExecutionContext, out var rawBundleRequestContext))
                {
                    var bundleResourceContext = JObject.Parse(rawBundleRequestContext.FirstOrDefault()).ToObject<BundleResourceContext>();

                    // BundleOperationId is only set for parallel bundles (non-empty Guid). Sequential bundles have Guid.Empty
                    isParallel = bundleResourceContext?.BundleOperationId != Guid.Empty;
                }

                // wait for the validation checks to pass before allowing the FHIRController action to continue
                await _searchParameterValidator.ValidateSearchParameterInput(
                    searchParameter,
                    context.HttpContext.Request.Method,
                    context.HttpContext.RequestAborted,
                    !isParallel); // Refreshing cache makes sense only for sequential bundles.
            }

            await next();
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
