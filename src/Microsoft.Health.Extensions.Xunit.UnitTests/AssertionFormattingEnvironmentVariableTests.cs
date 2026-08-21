// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Reflection;
using Xunit;
using Xunit.Sdk;

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    /// <summary>
    /// Pins the environment variable names the custom executor copies xunit's assertion formatting
    /// options into.
    /// </summary>
    /// <remarks>
    /// The executor substitutes its own assembly runner, so it does not go through the base
    /// implementation that normally makes this copy and has to make it itself. xunit keeps the names
    /// in an internal class that cannot be referenced, so the executor repeats them as literals. These
    /// tests read that class back by reflection and compare, so a rename in a future xunit version
    /// fails here rather than leaving options such as <c>--print-max-object-depth</c> accepted on the
    /// command line and then quietly doing nothing.
    /// </remarks>
    public class AssertionFormattingEnvironmentVariableTests
    {
        /// <summary>
        /// Checks one option's environment variable name against the one xunit reads it from.
        /// </summary>
        /// <param name="fieldName">The name of the field holding the variable name in xunit.</param>
        /// <param name="expectedVariableName">The literal the executor writes the option to.</param>
        [Theory]
        [InlineData("AssertEquivalentMaxDepth", CustomXunitTestFrameworkExecutor.AssertEquivalentMaxDepthVariable)]
        [InlineData("PrintMaxEnumerableLength", CustomXunitTestFrameworkExecutor.PrintMaxEnumerableLengthVariable)]
        [InlineData("PrintMaxObjectDepth", CustomXunitTestFrameworkExecutor.PrintMaxObjectDepthVariable)]
        [InlineData("PrintMaxObjectMemberCount", CustomXunitTestFrameworkExecutor.PrintMaxObjectMemberCountVariable)]
        [InlineData("PrintMaxStringLength", CustomXunitTestFrameworkExecutor.PrintMaxStringLengthVariable)]
        public void GivenAnAssertionFormattingOption_WhenItsNameIsComparedToXunits_ThenTheyMatch(string fieldName, string expectedVariableName)
        {
            Type environmentVariables = typeof(ITestFrameworkExecutionOptions).Assembly
                .GetTypes()
                .SingleOrDefault(type => type.FullName == "Xunit.Internal.EnvironmentVariables");

            Assert.NotNull(environmentVariables);

            FieldInfo field = environmentVariables.GetField(fieldName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            Assert.NotNull(field);
            Assert.Equal(expectedVariableName, field.GetRawConstantValue());
        }
    }
}
