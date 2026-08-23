// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
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

        /// <summary>
        /// Pins which option goes into which variable, not just that the names are right.
        /// </summary>
        /// <remarks>
        /// Every one of these options is an <see cref="int"/>, so two of them swapped compiles, sets
        /// every variable, and leaves all five names correct - the only symptom is assertion failure
        /// messages truncating at the wrong place, which the name comparison above cannot see. Giving
        /// each option a distinct value is what makes a swap fail here.
        /// </remarks>
        [Fact]
        public void GivenEveryAssertionFormattingOption_WhenTheyAreCopiedToTheEnvironment_ThenEachReachesItsOwnVariable()
        {
            var options = new RecordingExecutionOptions();
            options.SetAssertEquivalentMaxDepth(11);
            options.SetPrintMaxEnumerableLength(12);
            options.SetPrintMaxObjectDepth(13);
            options.SetPrintMaxObjectMemberCount(14);
            options.SetPrintMaxStringLength(15);

            Dictionary<string, int?> environment = CustomXunitTestFrameworkExecutor
                .BuildAssertionFormattingEnvironment(options)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

            Assert.Equal(
                new Dictionary<string, int?>(StringComparer.Ordinal)
                {
                    [CustomXunitTestFrameworkExecutor.AssertEquivalentMaxDepthVariable] = 11,
                    [CustomXunitTestFrameworkExecutor.PrintMaxEnumerableLengthVariable] = 12,
                    [CustomXunitTestFrameworkExecutor.PrintMaxObjectDepthVariable] = 13,
                    [CustomXunitTestFrameworkExecutor.PrintMaxObjectMemberCountVariable] = 14,
                    [CustomXunitTestFrameworkExecutor.PrintMaxStringLengthVariable] = 15,
                },
                environment);
        }

        /// <summary>
        /// An option nobody passed has to stay unset rather than being written as some default. The
        /// variables are process-wide and outlive the run that set them, so writing a value xunit
        /// would otherwise have chosen for itself would silently override whatever the environment
        /// already said.
        /// </summary>
        [Fact]
        public void GivenNoAssertionFormattingOptions_WhenTheyAreCopiedToTheEnvironment_ThenNoVariableIsGivenAValue()
        {
            IReadOnlyList<KeyValuePair<string, int?>> environment =
                CustomXunitTestFrameworkExecutor.BuildAssertionFormattingEnvironment(new RecordingExecutionOptions());

            // Asserting only that every value is null would hold just as well for a list that named no
            // variables at all, which is the one result that would mean the options are never read.
            Assert.NotEmpty(environment);
            Assert.All(environment, pair => Assert.Null(pair.Value));
        }

        /// <summary>
        /// The smallest thing that can stand in for the runner's options: xunit's own extension
        /// methods supply the option names, so the test states only the pairing and never repeats a
        /// name that could drift from the one the runner really uses.
        /// </summary>
        private sealed class RecordingExecutionOptions : ITestFrameworkExecutionOptions
        {
            private readonly Dictionary<string, object> _values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            public TValue GetValue<TValue>(string name)
            {
                return _values.TryGetValue(name, out object value) ? (TValue)value : default;
            }

            public void SetValue<TValue>(string name, TValue value)
            {
                _values[name] = value;
            }

            public string ToJson()
            {
                return "{}";
            }
        }
    }
}
