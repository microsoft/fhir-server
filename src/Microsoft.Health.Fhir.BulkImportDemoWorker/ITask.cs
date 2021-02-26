// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.BulkImportDemoWorker
{
    public interface ITask
    {
        public Task ExecuteAsync(IProgress<string> contextProgress, CancellationToken cancellationToken);
    }
}
