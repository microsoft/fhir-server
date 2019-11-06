// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Buffers;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Api.Features.Formatters
{
    internal class JsonArrayPool : IArrayPool<char>
    {
        private readonly ArrayPool<char> _inner;

        public JsonArrayPool(ArrayPool<char> inner)
        {
            _inner = inner;
        }

        public char[] Rent(int minimumLength)
        {
            return _inner.Rent(minimumLength);
        }

        public void Return(char[] array)
        {
            _inner.Return(array);
        }
    }
}
