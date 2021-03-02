// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.BulkImportDemoWorker
{
    public interface IStep<T>
    {
        void Start(IProgress<T> progress);

        Task WaitForStopAsync();
    }
}
