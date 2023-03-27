// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    /// <summary>
    /// Provides SQL retry parameters Used to initialize SqlRetryService.
    /// </summary>
    public class SqlRetryServiceOptions
    {
        /// <summary>Used to bind this class to .NRT options data source.</summary>
        /// TODO: There is same string "SqlServer" defined in Microsoft.Health.SqlServer.Configs.SqlServerDataStoreConfiguration class (SectionName property).
        /// Microsoft.Health.SqlServer.Configs.SqlServerDataStoreConfiguration class has its own retry parameters for the old retry logic that is being depreciated.
        /// Once FhirServer is fully transitioned to the new retry logic, decision needs to be made if this retry logic will be exposed to all the projects that use
        /// Microsoft.Health.SqlServer.Configs.SqlServerDataStoreConfiguration. If so, then properties in this class can be moved to
        /// Microsoft.Health.SqlServer.Configs.SqlServerDataStoreConfiguration. If not, then we can still reuse
        /// Microsoft.Health.SqlServer.Configs.SqlServerDataStoreConfiguration.SectionName to initialize this property (SqlServer = SqlServerDataStoreConfiguration.SectionName;).
        /// Keep in mind side effects of having in one assembly defined const var then used in another assembly. If there is a change in const var value, then
        /// still both assemblies need to be recompiled!
        public const string SqlServer = "SqlServer";

        /// <summary>Maximum number of (re)try attempts on a retriable error.</summary>
        public int MaxRetries { get; set; } = 5;

        /// <summary>Delay between retry attempts.</summary>
        public int RetryMillisecondsDelay { get; set; } = 5000;

        /// <summary>
        /// List of retriable errors numbers to be removed from the internal list of default retriable error numbers, in order to customize the retry logic.
        /// For definition of error numbers <see cref="SqlERxception.Number"/>.
        /// </summary>
        public IList<int> RemoveTransientErrors { get; } = new List<int>();

        /// <summary>
        /// List of retriable errors numbers to be added to the internal list of default retriable error numbers, in order to customize the retry logic.
        /// For definition of error numbers <see cref="SqlERxception.Number"/>.
        /// </summary>
        public IList<int> AddTransientErrors { get; } = new List<int>();
    }
}
