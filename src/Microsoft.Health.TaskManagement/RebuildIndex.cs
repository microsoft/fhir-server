// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.TaskManagement
{
    public class RebuildIndex
    {
        public string TableName { get; set; }

        public string IndexName { get; set; }

        public string Command { get; set; }

        public int Pid { get; set; }
    }
}
