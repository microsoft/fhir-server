// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;

namespace Microsoft.Health.Fhir.Api.Features.Resources.Bundle;

public sealed class BundleIfElse
{
    private readonly FhirJsonWriter _writer;
    private readonly bool _hasRun;

    internal BundleIfElse(FhirJsonWriter writer, bool hasRun)
    {
        EnsureArg.IsNotNull(writer, nameof(writer));

        _writer = writer;
        _hasRun = hasRun;
    }

    public FhirJsonWriter ElseIf(bool predicate, Action<FhirJsonWriter> action)
    {
        EnsureArg.IsNotNull(action, nameof(action));

        if (!_hasRun && predicate)
        {
            action(_writer);
        }

        return _writer;
    }
}
