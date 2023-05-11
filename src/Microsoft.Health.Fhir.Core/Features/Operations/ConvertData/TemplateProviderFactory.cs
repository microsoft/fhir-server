// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using EnsureThat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Operations.ConvertData.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.ConvertData
{
    public class TemplateProviderFactory : ITemplateProviderFactory
    {
        private readonly IServiceProvider _sp;

        public TemplateProviderFactory(IServiceProvider sp)
        {
            _sp = EnsureArg.IsNotNull(sp, nameof(sp));
        }

        public IConvertDataTemplateProvider GetContainerRegistryTemplateProvider()
        {
            IConvertDataTemplateProvider templateProvider;

            try
            {
                templateProvider = _sp.GetRequiredService<ContainerRegistryTemplateProvider>();
            }
            catch (InvalidOperationException ex)
            {
                throw new AzureContainerRegistryTokenException(Core.Resources.CannotGetContainerRegistryAccessToken, HttpStatusCode.Unauthorized, ex);
            }

            return templateProvider;
        }

        public IConvertDataTemplateProvider GetDefaultTemplateProvider()
        {
            return _sp.GetRequiredService<DefaultTemplateProvider>();
        }
    }
}
