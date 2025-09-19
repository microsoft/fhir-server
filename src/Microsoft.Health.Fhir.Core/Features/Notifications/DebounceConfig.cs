// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Notifications
{
    /// <summary>
    /// Delegate for processing actions after debounce delay.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the processing operation.</param>
    /// <returns>Task representing the processing operation.</returns>
    public delegate Task ProcessingAction(CancellationToken cancellationToken);

    /// <summary>
    /// Configuration for debounced processing.
    /// </summary>
    public class DebounceConfig
    {
        /// <summary>
        /// The delay in milliseconds for debouncing.
        /// Set to 0 or negative value for immediate processing without debouncing.
        /// </summary>
        public int DelayMs { get; set; }

        /// <summary>
        /// The action to execute after debouncing. Required.
        /// </summary>
        public ProcessingAction ProcessingAction { get; set; } = null!;

        /// <summary>
        /// Optional identifier for logging purposes.
        /// </summary>
        public string ProcessingName { get; set; } = "notification";

        /// <summary>
        /// Validates that the configuration is properly initialized.
        /// </summary>
        public void Validate()
        {
            ArgumentNullException.ThrowIfNull(ProcessingAction, nameof(ProcessingAction));
            ArgumentException.ThrowIfNullOrWhiteSpace(ProcessingName, nameof(ProcessingName));
        }
    }
}
