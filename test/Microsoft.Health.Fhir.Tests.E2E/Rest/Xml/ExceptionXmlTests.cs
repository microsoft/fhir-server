// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Tests.Common;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Xml
{
    [Trait(Traits.Category, Categories.Xml)]
    public class ExceptionXmlTests : ExceptionTests<XmlTestFixture<StartupWithThrowingMiddleware>>
    {
        public ExceptionXmlTests(XmlTestFixture<StartupWithThrowingMiddleware> fixture)
            : base(fixture)
        {
        }
    }
}
