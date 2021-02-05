// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Search
{
    public class SortValueJObjectGenerator : ISearchValueVisitor
    {
        private string _prefix;
        private bool _isHigh;

        private JObject CurrentEntry { get; set; }

        public JObject Generate(SortValue entry)
        {
            EnsureArg.IsNotNull(entry, nameof(entry));

            CreateEntry();

            if (entry.Low != null)
            {
                _prefix = "l";
                _isHigh = false;
                entry.Low.AcceptVisitor(this);
            }
            else
            {
                AddProperty("l", null);
            }

            if (entry.Hi != null)
            {
                _prefix = "h";
                _isHigh = true;
                entry.Hi.AcceptVisitor(this);
            }
            else
            {
                AddProperty("h", null);
            }

            return CurrentEntry;
        }

        public void Visit(CompositeSearchValue composite)
        {
        }

        public void Visit(DateTimeSearchValue dateTime)
        {
            if (_isHigh)
            {
                AddProperty(_prefix, dateTime.End.ToString("o", CultureInfo.InvariantCulture));
            }
            else
            {
                AddProperty(_prefix, dateTime.Start.ToString("o", CultureInfo.InvariantCulture));
            }
        }

        public void Visit(NumberSearchValue number)
        {
            if (_isHigh)
            {
                AddProperty(_prefix, number.High ?? number.Low);
            }
            else
            {
                AddProperty(_prefix, number.Low ?? number.High);
            }
        }

        public void Visit(QuantitySearchValue quantity)
        {
            if (_isHigh)
            {
                AddProperty(_prefix, quantity.High ?? quantity.Low);
            }
            else
            {
                AddProperty(_prefix, quantity.Low ?? quantity.High);
            }
        }

        public void Visit(ReferenceSearchValue reference)
        {
            AddProperty(_prefix, reference.ToString().ToUpperInvariant());
        }

        public void Visit(StringSearchValue s)
        {
            AddProperty(_prefix, s.String.ToUpperInvariant());
        }

        public void Visit(TokenSearchValue token)
        {
        }

        public void Visit(UriSearchValue uri)
        {
            AddProperty(_prefix, uri.Uri);
        }

        private void CreateEntry()
        {
            CurrentEntry = new JObject();
        }

        private void AddProperty(string name, object value)
        {
            CurrentEntry.Add(new JProperty(name, value));
        }
    }
}
