// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;

namespace Microsoft.Health.Fhir.Web;

[SuppressMessage("Design", "CA1812", Justification = "False positive.")]
internal sealed class DummyMeterFactory : IMeterFactory
{
    public Meter Create(MeterOptions options) => new(options);

    public void Dispose()
    {
    }
}
