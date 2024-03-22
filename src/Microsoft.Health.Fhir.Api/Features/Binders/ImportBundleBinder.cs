// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;

namespace Microsoft.Health.Fhir.Api.Features.Binders;

public class ImportBundleBinder : IModelBinder
{
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Returned as Model")]
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        EnsureArg.IsNotNull(bindingContext, nameof(bindingContext));

        bindingContext.Result = ModelBindingResult.Success(
            new ImportBundleParser(bindingContext.HttpContext.Request.BodyReader.AsStream()));

        return Task.CompletedTask;
    }
}
