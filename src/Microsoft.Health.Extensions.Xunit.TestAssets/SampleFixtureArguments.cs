// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Extensions.Xunit.TestAssets
{
    /// <summary>
    /// Stands in for the real DataStore fixture argument dimension.
    /// </summary>
    [Flags]
    public enum SampleDataStore
    {
        /// <summary>No data store.</summary>
        None = 0,

        /// <summary>The first data store.</summary>
        StoreA = 1,

        /// <summary>The second data store.</summary>
        StoreB = 2,

        /// <summary>All data stores.</summary>
        All = StoreA | StoreB,
    }

    /// <summary>
    /// Stands in for the real Format fixture argument dimension.
    /// </summary>
    [Flags]
    public enum SampleFormat
    {
        /// <summary>No format.</summary>
        None = 0,

        /// <summary>The first format.</summary>
        FormatA = 1,

        /// <summary>The second format.</summary>
        FormatB = 2,

        /// <summary>All formats.</summary>
        All = FormatA | FormatB,
    }

    /// <summary>
    /// The sample equivalent of HttpIntegrationFixtureArgumentSetsAttribute.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false)]
    public sealed class SampleFixtureArgumentSetsAttribute : FixtureArgumentSetsAttribute
    {
        public SampleFixtureArgumentSetsAttribute(SampleDataStore dataStores = 0, SampleFormat formats = 0)
            : base(dataStores, formats)
        {
        }
    }

    /// <summary>
    /// A class fixture whose constructor arguments come from the fixture argument sets.
    /// </summary>
    public sealed class SampleFixture
    {
        public SampleFixture(SampleDataStore dataStore, SampleFormat format)
        {
            DataStore = dataStore;
            Format = format;
        }

        public SampleDataStore DataStore { get; }

        public SampleFormat Format { get; }
    }
}
