// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.CosmosDb.Features.Search;
using Microsoft.Health.Fhir.CosmosDb.Features.Search.Legacy;
using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Search
{
    internal class SearchIndexEntryJObjectGenerator : ISearchValueVisitor
    {
        public SearchIndexEntryJObjectGenerator()
        {
            Reset();
        }

        private int? Index { get; set; }

        private bool ExcludeTokenText { get; set; }

        public JObject Output
        {
            get;
            private set;
        }

        public void Visit(LegacyCompositeSearchValue composite)
        {
            AddProperty(LegacySearchValueConstants.CompositeSystemName, composite.System);
            AddProperty(LegacySearchValueConstants.CompositeCodeName, composite.Code);

            composite.Value.AcceptVisitor(this);
        }

        public void Visit(CompositeSearchValue composite)
        {
            try
            {
                // Set the component index and process individual component of the composite value.
                for (int i = 0; i < composite.Components.Count; i++)
                {
                    Index = i;
                    ExcludeTokenText = true;

                    composite.Components[i].AcceptVisitor(this);
                }
            }
            finally
            {
                Index = null;
                ExcludeTokenText = false;
            }
        }

        public void Visit(DateTimeSearchValue dateTime)
        {
            // By default, Json.NET will serialize date time object using format
            // "yyyy'-'MM'-'dd'T'HH':'mm':'ss.FFFFFFFK". The 'F' format specifier
            // will not display if the value is 0. For example, 2018-01-01T00:00:00.0000000+00:00
            // is formatted as 2018-01-01T00:00:00+00:00 but 2018-01-01T00:00:00.9999999+00:00 is
            // formatted as 2018-01-01T00:00:00.9999999+00:00. Because Cosmos DB only supports range index
            // with string or number data type, the comparison does not work correctly in some cases.
            // Output the date time using 'o' to make sure the fraction is always generated.
            AddProperty(SearchValueConstants.DateTimeStartName, dateTime.Start.ToString("o", CultureInfo.InvariantCulture));
            AddProperty(SearchValueConstants.DateTimeEndName, dateTime.End.ToString("o", CultureInfo.InvariantCulture));
        }

        public void Visit(NumberSearchValue number)
        {
            AddProperty(SearchValueConstants.NumberName, number.Number);
        }

        public void Visit(QuantitySearchValue quantity)
        {
            AddProperty(SearchValueConstants.SystemName, quantity.System);
            AddProperty(SearchValueConstants.CodeName, quantity.Code);
            AddProperty(SearchValueConstants.QuantityName, quantity.Quantity);
        }

        public void Visit(ReferenceSearchValue reference)
        {
            AddProperty(SearchValueConstants.ReferenceName, reference.Reference);
        }

        public void Visit(StringSearchValue s)
        {
            AddProperty(SearchValueConstants.StringName, s.String);
            AddProperty(SearchValueConstants.NormalizedStringName, s.String.ToUpperInvariant());
        }

        public void Visit(TokenSearchValue token)
        {
            AddIfNotNull(SearchValueConstants.SystemName, token.System);
            AddIfNotNull(SearchValueConstants.CodeName, token.Code);

            if (!ExcludeTokenText)
            {
                // Since text is case-insensitive search, it will always be normalized.
                AddIfNotNull(SearchValueConstants.NormalizedTextName, token.Text?.ToUpperInvariant());
            }

            void AddIfNotNull(string name, string value)
            {
                if (value != null)
                {
                    AddProperty(name, value);
                }
            }
        }

        public void Visit(UriSearchValue uri)
        {
            AddProperty(SearchValueConstants.UriName, uri.Uri);
        }

        public void Reset()
        {
            Output = new JObject();
        }

        private void AddProperty(string name, object value)
        {
            if (Index != null)
            {
                name = $"{name}_{Index}";
            }

            Output.Add(new JProperty(name, value));
        }
    }
}
