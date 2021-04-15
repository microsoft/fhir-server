// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public interface IBulkRawResourceProcessor
    {
        public Task<long> ProcessingDataAsync(Channel<string> inputChannel, Channel<BulkImportResourceWrapper> outputChannel, long startSurrogateId, CancellationToken cancellationToken);
    }
}
