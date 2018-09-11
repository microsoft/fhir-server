// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Api.Features.Binders
{
    public class PartialDateTimeBinder : IModelBinder
    {
        public Task BindModelAsync(ModelBindingContext bindingContext)
        {
            EnsureArg.IsNotNull(bindingContext, nameof(bindingContext));

            var valueProviderResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);

            if (valueProviderResult.Any() && !string.IsNullOrWhiteSpace(valueProviderResult.FirstValue))
            {
                try
                {
                    var result = PartialDateTime.Parse(valueProviderResult.FirstValue);

                    bindingContext.Result = ModelBindingResult.Success(result);
                }
                catch
                {
                    bindingContext.Result = ModelBindingResult.Failed();
                }
            }

            return Task.CompletedTask;
        }
    }
}
