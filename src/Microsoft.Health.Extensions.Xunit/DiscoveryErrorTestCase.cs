// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Xunit.v3;

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// A test case standing in for one test that a failure to expand fixture argument sets lost,
    /// carrying the traits the expansion never got far enough to produce.
    /// </summary>
    /// <remarks>
    /// A fixture argument set class is normally expanded into one variant per argument, and each
    /// variant carries a trait naming its argument. Legs that select tests by those traits are the
    /// reason this type exists: <see cref="ExecutionErrorTestCase"/> takes no traits and so would be
    /// dropped by a positive trait filter, which puts the run back to passing with the broken class
    /// silently missing - the exact outcome reporting the fault as a test was meant to prevent.
    /// <para>
    /// One case carries one combination of argument set values, never all of them at once. A trait
    /// filter matches a case when <em>any</em> value under the named trait matches, and an exclusion
    /// filter is the negation of that, so a single case declaring both <c>DataStore=CosmosDb</c> and
    /// <c>DataStore=SqlServer</c> would be excluded by both <c>--filter-not-trait
    /// DataStore=SqlServer</c> and <c>--filter-not-trait DataStore=CosmosDb</c> - that is, by every
    /// leg this repository runs. The discoverer therefore reports one of these per combination, so
    /// each leg selects exactly the case that stands for the tests it lost.
    /// </para>
    /// </remarks>
    internal sealed class DiscoveryErrorTestCase : ExecutionErrorTestCase
    {
        public DiscoveryErrorTestCase(
            IXunitTestMethod testMethod,
            string testCaseDisplayName,
            string uniqueID,
            string errorMessage,
            IReadOnlyDictionary<string, IReadOnlyCollection<string>> traits)
            : base(testMethod, testCaseDisplayName, uniqueID, sourceFilePath: null, sourceLineNumber: null, errorMessage: errorMessage)
        {
            EnsureArg.IsNotNull(traits, nameof(traits));

            // XunitTestCase.Traits is the live dictionary the case is filtered on, and the base
            // constructor has already created it empty. Adding to it here is what gives the failure
            // the traits of the tests it replaces.
            foreach (KeyValuePair<string, IReadOnlyCollection<string>> trait in traits)
            {
                if (!Traits.TryGetValue(trait.Key, out HashSet<string> values))
                {
                    values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    Traits[trait.Key] = values;
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
