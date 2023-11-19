// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;

namespace Microsoft.Health.Fhir.Core.Logging
{
    public abstract class BaseOperationStatistics
    {
        protected BaseOperationStatistics()
        {
            Stopwatch = new Stopwatch();
        }

        protected Stopwatch Stopwatch { get; private set; }

        public abstract string GetStatisticsAsJson();

        public abstract string GetLoggingCategory();

        public void StartCollectingResults()
        {
            Stopwatch.Start();
        }

        public void StopCollectingResults()
        {
            Stopwatch.Stop();
        }
    }
}
