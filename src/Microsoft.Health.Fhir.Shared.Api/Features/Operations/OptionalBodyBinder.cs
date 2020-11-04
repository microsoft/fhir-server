// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.WebUtilities;

namespace Microsoft.Health.Fhir.Api.Features.Operations
{
    /// <summary>
    /// Using the [FromBody] attribute mandates that there is body content, this binder makes that optional again.
    /// </summary>
    public class OptionalBodyBinder : IModelBinder
    {
        private readonly IEnumerable<TextInputFormatter> _inputFormatters;

        public OptionalBodyBinder(IEnumerable<TextInputFormatter> inputFormatters)
        {
            EnsureArg.IsNotNull(inputFormatters);

            _inputFormatters = inputFormatters;
        }

        public async Task BindModelAsync(ModelBindingContext bindingContext)
        {
            EnsureArg.IsNotNull(bindingContext);

            var inputFormatterContext = new InputFormatterContext(
                bindingContext.HttpContext,
                bindingContext.ModelName,
                bindingContext.ModelState,
                bindingContext.ModelMetadata,
                (stream, encoding) => new HttpRequestStreamReader(stream, encoding));

            TextInputFormatter inputFormatter = _inputFormatters.FirstOrDefault(x => x.CanRead(inputFormatterContext));

            if (inputFormatter != null)
            {
                InputFormatterResult result = await inputFormatter.ReadAsync(inputFormatterContext);

                if (result.HasError)
                {
                    bindingContext.Result = ModelBindingResult.Failed();
                    return;
                }

                if (result.IsModelSet)
                {
                    bindingContext.Model = result.Model;
                    bindingContext.Result = ModelBindingResult.Success(result.Model);
                    return;
                }
            }

            bindingContext.Result = ModelBindingResult.Success(null);
        }
    }
}
