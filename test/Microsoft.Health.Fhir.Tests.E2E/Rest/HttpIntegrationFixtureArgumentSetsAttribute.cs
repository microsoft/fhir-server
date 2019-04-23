// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Extensions.Xunit;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false)]
    public sealed class HttpIntegrationFixtureArgumentSetsAttribute : FixtureArgumentSetsAttribute
    {
        public HttpIntegrationFixtureArgumentSetsAttribute(DataStore dataStores = 0, Format formats = 0)
            : base(dataStores, formats)
        {
        }
    }
}
