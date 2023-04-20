// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Health.Fhir.Core.Features.Operations.ConvertData
{
    public class TemplateProviderFactory
    {
        private IServiceProvider _sp;

        public TemplateProviderFactory(IServiceProvider sp)
        {
            EnsureArg.IsNotNull(sp, nameof(sp));

            _sp = sp;
        }

        public IConvertDataTemplateProvider GetContainerRegistryTemplateProvider()
        {
            return _sp.GetRequiredService<ContainerRegistryTemplateProvider>();
        }

        public IConvertDataTemplateProvider GetDefaultTemplateProvider()
        {
            return _sp.GetRequiredService<DefaultTemplateProvider>();
        }
    }
}
