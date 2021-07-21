// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
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

        public PurgeOperationCapabilityProvider(IUrlResolver resolver)
        {
            EnsureArg.IsNotNull(resolver, nameof(resolver));

            _resolver = resolver;
        }

        public void Build(ICapabilityStatementBuilder builder)
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            builder.Apply(capabilityStatement =>
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
            });
        }
    }
}
