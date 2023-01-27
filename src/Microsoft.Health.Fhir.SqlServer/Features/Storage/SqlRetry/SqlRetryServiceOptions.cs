// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    public class SqlRetryServiceOptions
    {
        public const string SqlServer = "SqlServer";

        public int MaxRetries { get; set; } = 5;

        public int RetryMillisecondsDelay { get; set; } = 5000;

        public IList<int> RemoveTransientErrors { get; } = new List<int>();

        public IList<int> AddTransientErrors { get; } = new List<int>();
    }
}
