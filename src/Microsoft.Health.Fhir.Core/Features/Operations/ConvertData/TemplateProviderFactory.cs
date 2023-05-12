// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using EnsureThat;
using Microsoft.Build.Framework;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Operations.ConvertData.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.ConvertData
{
    public class TemplateProviderFactory : ITemplateProviderFactory
    {
        private readonly IServiceProvider _sp;
        private readonly ILogger<TemplateProviderFactory> _logger;

        public TemplateProviderFactory(IServiceProvider sp, ILogger<TemplateProviderFactory> logger)
        {
            _sp = EnsureArg.IsNotNull(sp, nameof(sp));
            _logger = logger;
        }

        public IConvertDataTemplateProvider GetContainerRegistryTemplateProvider()
        {
            IConvertDataTemplateProvider templateProvider;

            try
            {
                templateProvider = _sp.GetRequiredService<ContainerRegistryTemplateProvider>();
            }
            catch (Exception ex)
            {
                _logger.LogError(exception: ex, message: "Failed to resolve ContainerRegistryTemplateProvider dependency.");
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
