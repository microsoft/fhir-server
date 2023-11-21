// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;

namespace Microsoft.Health.Fhir.Core.Logging
{
    public abstract class BaseOperationStatistics
    {
        private readonly Stopwatch _stopwatch;

        protected BaseOperationStatistics()
        {
            _stopwatch = new Stopwatch();
        }

        public long ElapsedMilliseconds
        {
            get { return _stopwatch.ElapsedMilliseconds; }
        }

        public abstract string GetStatisticsAsJson();

        public abstract string GetLoggingCategory();

        public virtual void StartCollectingResults()
        {
            _stopwatch.Start();
        }

        public virtual void StopCollectingResults()
        {
            _stopwatch.Stop();
        }
    }
}
