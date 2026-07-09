// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.Core.Features.Sdk
{
    /// <summary>
    /// Guards known compatibility fallbacks between SDK implementations.
    /// </summary>
    public class SdkFallbackGuard : ISdkFallbackGuard
    {
        private readonly ISdkModeProvider _modeProvider;
        private readonly ILogger<SdkFallbackGuard> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="SdkFallbackGuard"/> class.
        /// </summary>
        /// <param name="modeProvider">The SDK mode provider.</param>
        /// <param name="logger">The logger.</param>
        public SdkFallbackGuard(ISdkModeProvider modeProvider, ILogger<SdkFallbackGuard> logger)
        {
            _modeProvider = EnsureArg.IsNotNull(modeProvider, nameof(modeProvider));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        /// <inheritdoc />
        public void FirelyFallback(string surface, string reason)
        {
            EnsureArg.IsNotNullOrWhiteSpace(surface, nameof(surface));
            EnsureArg.IsNotNullOrWhiteSpace(reason, nameof(reason));

            if (_modeProvider.IsIgnixaMode)
            {
                throw new InvalidOperationException($"Firely fallback is not allowed in Ignixa SDK mode. Surface: {surface}. Reason: {reason}.");
            }

            _logger.LogInformation("Firely SDK fallback used. Surface: {Surface}. Reason: {Reason}. Mode: {Mode}.", surface, reason, _modeProvider.Mode);
        }

        /// <inheritdoc />
        public void IgnixaFallback(string surface, string reason)
        {
            EnsureArg.IsNotNullOrWhiteSpace(surface, nameof(surface));
            EnsureArg.IsNotNullOrWhiteSpace(reason, nameof(reason));

            if (_modeProvider.IsFirelyMode)
            {
                throw new InvalidOperationException($"Ignixa fallback is not allowed in Firely SDK mode. Surface: {surface}. Reason: {reason}.");
            }

            _logger.LogInformation("Ignixa SDK fallback used. Surface: {Surface}. Reason: {Reason}. Mode: {Mode}.", surface, reason, _modeProvider.Mode);
        }
    }
}
