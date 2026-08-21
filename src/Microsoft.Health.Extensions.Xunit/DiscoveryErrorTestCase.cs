// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Reflection;
using EnsureThat;
using Xunit.v3;

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// A test case standing in for a class whose fixture argument sets could not be expanded, carrying
    /// the traits the expansion never got far enough to produce.
    /// </summary>
    /// <remarks>
    /// A fixture argument set class is normally expanded into one variant per argument, and each
    /// variant carries a trait naming its argument. Legs that select tests by those traits are the
    /// reason this type exists: <see cref="ExecutionErrorTestCase"/> takes no traits and so would be
    /// dropped by a positive trait filter, which puts the run back to passing with the broken class
    /// silently missing - the exact outcome reporting the fault as a test was meant to prevent.
    /// Declaring every value the class asked for means any filter that would have selected any of its
    /// variants also selects the failure.
    /// </remarks>
    internal sealed class DiscoveryErrorTestCase : ExecutionErrorTestCase
    {
        private static readonly FieldInfo TraitsField = typeof(XunitTestCase)
            .GetField("traits", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "XunitTestCase.traits was not found, so a discovery failure cannot be given the traits of the tests it " +
                "replaces and would be hidden by a trait filter. This usually means the xunit.v3 version changed.");

        public DiscoveryErrorTestCase(
            IXunitTestMethod testMethod,
            string testCaseDisplayName,
            string uniqueID,
            string errorMessage,
            IReadOnlyDictionary<string, IReadOnlyCollection<string>> traits)
            : base(testMethod, testCaseDisplayName, uniqueID, sourceFilePath: null, sourceLineNumber: null, errorMessage: errorMessage)
        {
            EnsureArg.IsNotNull(traits, nameof(traits));

            if (TraitsField.GetValue(this) is not Dictionary<string, HashSet<string>> existing)
            {
                return;
            }

            foreach (KeyValuePair<string, IReadOnlyCollection<string>> trait in traits)
            {
                if (!existing.TryGetValue(trait.Key, out HashSet<string> values))
                {
                    values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    existing[trait.Key] = values;
                }

                values.UnionWith(trait.Value);
            }
        }

#pragma warning disable CS0618 // Called by the de-serializer; should only be called by deriving classes for de-serialization purposes
        public DiscoveryErrorTestCase()
        {
        }
#pragma warning restore CS0618
    }
}
