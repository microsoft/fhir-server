// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.SqlServer.Features.Partitions;
using NSubstitute;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Storage
{
    internal sealed class SqlPartitionTestRequest : IDisposable
    {
        internal SqlPartitionTestRequest(string partitionId, string databaseName)
        {
            var context = Substitute.For<IFhirRequestContext>();
            context.Partition.Returns(new PartitionInfo { Id = partitionId, DatabaseName = databaseName });
            Accessor = new FhirRequestContextAccessor { RequestContext = context };
        }

        internal RequestContextAccessor<IFhirRequestContext> Accessor { get; }

        public void Dispose() => Accessor.RequestContext = null;
    }
}
