// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Net.Http.Headers;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    public class WeakETagBinder : IModelBinder
    {
        public Task BindModelAsync(ModelBindingContext bindingContext)
        {
            EnsureArg.IsNotNull(bindingContext, nameof(bindingContext));

            var suppliedWeakETag = bindingContext.HttpContext.Request.Headers[HeaderNames.IfMatch];

            WeakETag model = null;
            if (!string.IsNullOrWhiteSpace(suppliedWeakETag))
            {
                try
                {
                    model = WeakETag.FromWeakETag(suppliedWeakETag);
                }
                catch (BadRequestException ex)
                {
                    bindingContext.ModelState.AddModelError(bindingContext.ModelName, ex.Message);
                    bindingContext.Result = ModelBindingResult.Failed();
                }
            }

            bindingContext.Model = model;
            bindingContext.Result = ModelBindingResult.Success(model);

            return Task.CompletedTask;
        }
    }
}
