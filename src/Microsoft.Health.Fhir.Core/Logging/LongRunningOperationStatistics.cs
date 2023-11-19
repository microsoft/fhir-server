// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using EnsureThat;
using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Fhir.Core.Logging
{
    public sealed class LongRunningOperationStatistics : BaseOperationStatistics
    {
        private readonly string _operationName;

        private int _iterationCount;

        public LongRunningOperationStatistics(string operationName)
            : base()
        {
            EnsureArg.IsNotNull(operationName, nameof(operationName));

            _operationName = operationName;
            _iterationCount = 0;
        }

        public int IterationCount
        {
            get
            {
                return _iterationCount;
            }
        }

        public void Iterate()
        {
            Interlocked.Increment(ref _iterationCount);
        }

        public override string GetLoggingCategory() => _operationName;

        public override string GetStatisticsAsJson()
        {
            JObject serializableEntity = JObject.FromObject(new
            {
                label = GetLoggingCategory(),
                iterationCount = _iterationCount,
                executionTime = Stopwatch.ElapsedMilliseconds,
            });

            return serializableEntity.ToString();
        }
    }
}
