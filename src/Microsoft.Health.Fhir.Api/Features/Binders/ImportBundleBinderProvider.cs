// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;

namespace Microsoft.Health.Fhir.Api.Features.Binders
{
    public class ImportBundleBinderProvider : IModelBinderProvider
    {
        public IModelBinder GetBinder(ModelBinderProviderContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            if (context.Metadata.ModelType == typeof(ImportBundleParser))
            {
                return new ImportBundleBinder();
            }

            return null;
        }
    }
}
