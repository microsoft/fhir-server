// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Extensions.DependencyInjection;

namespace Microsoft.Health.Fhir.Shared.Tests.Integration
{
    public class TestScoped<T> : IScoped<T>
    {
        public T Value { get; set; }

        public void Dispose()
        {
            return;
        }
    }
}
