// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.Core.Features.Routing;

namespace Microsoft.Health.Fhir.Api.Features.Operations
{
    public class OperationsCapabilityProvider : IProvideCapability
    {
        private readonly OperationsConfiguration _operationConfiguration;
        private readonly IUrlResolver _urlResolver;

        public OperationsCapabilityProvider(IOptions<OperationsConfiguration> operationConfiguration, IUrlResolver urlResolver)
        {
            EnsureArg.IsNotNull(operationConfiguration?.Value, nameof(operationConfiguration));
            EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));

            _operationConfiguration = operationConfiguration.Value;
            _urlResolver = urlResolver;
        }

        public void Build(ICapabilityStatementBuilder builder)
        {
            if (_operationConfiguration.Export.Enabled)
            {
                builder.Update(AddExportDetails);
            }

            if (_operationConfiguration.Reindex.Enabled)
            {
                builder.Update(AddReindexDetails);
            }
        }

        public void AddExportDetails(ListedCapabilityStatement capabilityStatement)
        {
            Uri exportDefinitionUri = _urlResolver.ResolveOperationDefinitionUrl("export");
            capabilityStatement.Rest.Server().Operation.Add(new OperationComponent()
            {
                Name = "export",
                Definition = exportDefinitionUri.ToString(),
            });
        }

        public void AddReindexDetails(ListedCapabilityStatement capabilityStatement)
        {
            Uri reindexDefinitionUri = _urlResolver.ResolveOperationDefinitionUrl("reindex");
            capabilityStatement.Rest.Server().Operation.Add(new OperationComponent()
            {
                Name = "reindex",
                Definition = reindexDefinitionUri.ToString(),
            });
        }
    }
}
