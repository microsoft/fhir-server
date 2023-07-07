// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Audit
{
    public interface IMockLogger<T> : ILogger<T>
    {
        IList<Log> GetLogs();
    }
}
