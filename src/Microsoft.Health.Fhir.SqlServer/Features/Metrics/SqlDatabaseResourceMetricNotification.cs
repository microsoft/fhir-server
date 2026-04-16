// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.SqlServer.Features.Metrics
{
    internal sealed class SqlDatabaseResourceMetricNotification
    {
        public DateTimeOffset EndTime { get; init; }

        public string DatabaseType { get; init; } = "AzureSqlDatabase";

        public double CpuPercent { get; init; }

        public double DataIoPercent { get; init; }

        public double LogIoPercent { get; init; }

        public double MemoryPercent { get; init; }

        public double WorkersPercent { get; init; }

        public double SessionsPercent { get; init; }

        public double? InstanceCpuPercent { get; init; }

        public double? InstanceMemoryPercent { get; init; }
    }
}
