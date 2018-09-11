// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Common
{
    public class MockTests
    {
        [Fact]
        public void GivenAnInstance_WhenMockingAProperty_ThenThePropertyIsMockedAndReset()
        {
            var magic = "magic";
            var newvalue = "newValue";

            var p = new TestType
            {
                Property1 = magic,
            };

            using (Mock.Property(() => p.Property1, newvalue))
            {
                Assert.Equal(newvalue, p.Property1);
            }

            Assert.Equal(magic, p.Property1);
        }

        [Fact]
        public void GivenAStatic_WhenMockingAProperty_ThenThePropertyIsMockedAndReset()
        {
            var initial = "Initial";
            var newvalue = "newValue";

            Assert.Equal(initial, TestType.StaticProperty);

            using (Mock.Property(() => TestType.StaticProperty, newvalue))
            {
                Assert.Equal(newvalue, TestType.StaticProperty);
            }

            Assert.Equal(initial, TestType.StaticProperty);
        }

        [Fact]
        public void GivenAnInstance_WhenMockingAMethod_ThenANotSupportedExceptionIsThrown()
        {
            var p = new TestType();

            Assert.Throws<NotSupportedException>(() => Mock.Property(() => p.CallMe(), "test"));
        }

        [Fact]
        public void GivenAType_WhenMockingAnInstance_TheConstructorWithLeastArgumentsIsUsed()
        {
            var instance = Mock.TypeWithArguments<TestTypeWithArgs>();

            Assert.NotNull(instance);
            Assert.NotNull(instance.OneArg);
            Assert.Null(instance.SecondArg);
        }

        [Fact]
        public void GivenAType_WhenMockingAnInstance_ParametersCanBeUsed()
        {
            var parameter = new TestType();
            var instance = Mock.TypeWithArguments<TestTypeWithArgs>(parameter);

            Assert.Equal(parameter, instance.OneArg);
        }

        [Fact]
        public void GivenAType_WhenMockingAnInstance_ParameterWithDerivedTypeCanBeUsed()
        {
            var parameter = new DerivedTestType();
            var instance = Mock.TypeWithArguments<TestTypeWithArgs>(parameter);

            Assert.Equal(parameter, instance.OneArg);
        }

        private class DerivedTestType : TestType
        {
        }
    }
}
