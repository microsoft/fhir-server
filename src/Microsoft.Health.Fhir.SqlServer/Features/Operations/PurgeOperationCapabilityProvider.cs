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
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Schema;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations
{
    public class PurgeOperationCapabilityProvider : IProvideCapability
    {
        private readonly SchemaInformation _schemaInformation;
        private readonly IUrlResolver _resolver;

        public PurgeOperationCapabilityProvider(
            SchemaInformation schemaInformation,
            IUrlResolver resolver)
        {
            EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
            EnsureArg.IsNotNull(resolver, nameof(resolver));

            _schemaInformation = schemaInformation;
            _resolver = resolver;
        }

        public void Build(ICapabilityStatementBuilder builder)
        {
            if (_schemaInformation.Current >= SchemaVersionConstants.PurgeHistoryVersion)
            {
                builder.Apply(capabilityStatement =>
                {
                    Uri operationDefinitionUri = _resolver.ResolveOperationDefinitionUrl(OperationsConstants.PurgeHistory);
                    capabilityStatement.Rest.Server().Operation.Add(new OperationComponent
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
}
