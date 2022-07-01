// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.JobManagement.UnitTests
{
    public class TestJob : IJob
    {
        private Func<IProgress<string>, CancellationToken, Task<string>> _executeFunc;

        public TestJob(Func<IProgress<string>, CancellationToken, Task<string>> executeFunc)
        {
            _executeFunc = executeFunc;
        }

        public async Task<string> ExecuteAsync(IProgress<string> progress, CancellationToken cancellationToken)
        {
            return await _executeFunc(progress, cancellationToken);
        }
    }
}
