// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.TaskManagement
{
    public interface IContextUpdaterFactory
    {
        public IContextUpdater CreateContextUpdater(string taskId, string runId);
    }
}
