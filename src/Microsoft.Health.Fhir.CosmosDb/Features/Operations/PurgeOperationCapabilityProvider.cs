// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Operations
{
    public class PurgeOperationCapabilityProvider : IProvideCapability
    {
        private readonly IUrlResolver _resolver;
        private readonly ILogger<PurgeOperationCapabilityProvider> _logger;

        public PurgeOperationCapabilityProvider(
            IUrlResolver resolver,
            ILogger<PurgeOperationCapabilityProvider> logger)
        {
            EnsureArg.IsNotNull(resolver, nameof(resolver));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _resolver = resolver;
            _logger = logger;
        }

        public void Build(ICapabilityStatementBuilder builder)
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            builder.Apply(capabilityStatement =>
            {
                try
                {
                    Uri operationDefinitionUri = _resolver.ResolveOperationDefinitionUrl(OperationsConstants.PurgeHistory);
                    capabilityStatement.Rest.Server().Operation.Add(new OperationComponent()
                    {
                        Name = OperationsConstants.PurgeHistory,
                        Definition = new ReferenceComponent
                        {
                            Reference = operationDefinitionUri.ToString(),
                        },
                    });
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "PurgeOperationCapabilityProvider failed creating a new Capability Statement.");
                    throw;
                }
            });
        }
    }
}
