// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Messages.ConvertData;

namespace Microsoft.Health.Fhir.Core.Features.Operations.ConvertData
{
    public class TemplateProviderFactory
    {
        private ContainerRegistryTemplateProvider _containerRegistryTemplateProvider;
        private DefaultTemplateProvider _defaultTemplateProvider;

        public TemplateProviderFactory(ContainerRegistryTemplateProvider containerRegistryTemplateProvider, DefaultTemplateProvider defaultTemplateProvider) 
        {
            _containerRegistryTemplateProvider = containerRegistryTemplateProvider;
            _defaultTemplateProvider = defaultTemplateProvider;
        }

        public IConvertDataTemplateProvider GetTemplateProvider(ConvertDataRequest request)
        {
            if (request.IsDefaultTemplateReference)
            {
                return _defaultTemplateProvider;
            }
            else
            {
                return _containerRegistryTemplateProvider;
            }
        }
    }
}
