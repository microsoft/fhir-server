// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Xunit;

namespace Microsoft.Health.Extensions.Xunit
{
    public class NotTest
    {
        /// <summary>
        /// Once xunit is added to the project, test discoverer will try to find tests in the project. If no test is found, test discoverer will issue a warning.
        /// To avoid this warning we add the test to the project.
        /// </summary>
        [Fact]
        public void AlwaysSucceed()
        {
            Assert.True(true);
        }
    }
}
