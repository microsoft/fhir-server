// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Everything
{
    public class EverythingOperationContinuationToken
    {
        public EverythingOperationContinuationToken(int phase, string internalContinuationToken)
        {
            Phase = phase;
            InternalContinuationToken = internalContinuationToken;
        }

        public int Phase { get; private set; }

        public string InternalContinuationToken { get; private set; }

        public static string ToString(int phase, string internalContinuationToken)
        {
            return JsonSerializer.Serialize(new EverythingOperationContinuationToken(phase, internalContinuationToken));
        }

        public static EverythingOperationContinuationToken FromString(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<EverythingOperationContinuationToken>(json);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
