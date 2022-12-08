// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Buffers;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Api.Features.Formatters
{
    /// <summary>
    /// This adapts the Json.net IArrayPool to the .NET ArrayPool
    /// </summary>
    internal class JsonArrayPool : IArrayPool<char>
    {
        private const int MaxArrayPoolRentSizeInBytes = 100000000; // 100MB.

        private readonly ArrayPool<char> _inner;

        public JsonArrayPool(ArrayPool<char> inner)
        {
            _inner = inner;
        }

        public char[] Rent(int minimumLength)
        {
            if (minimumLength >= MaxArrayPoolRentSizeInBytes)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minimumLength),
                    $"The size of the array attempted to be rent is greater or equal than 100MB. Value {minimumLength}.");
            }

            return _inner.Rent(minimumLength);
        }

        public void Return(char[] array)
        {
            _inner.Return(array);
        }
    }
}
