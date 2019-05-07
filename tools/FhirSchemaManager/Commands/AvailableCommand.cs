// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.CommandLine.Invocation;
using System.CommandLine.Rendering;
using System.CommandLine.Rendering.Views;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace FhirSchemaManager.Commands
{
    public static class AvailableCommand
    {
        public static async Task Handler(InvocationContext invocationContext, Uri fhirServer)
        {
            var region = new Region(
                0,
                0,
                Console.WindowWidth,
                Console.WindowHeight,
                true);

            var httpClient = new HttpClient
            {
                BaseAddress = fhirServer,
            };

            var jsonResult = await httpClient.GetStringAsync("/_schema/versions");

            List<JToken> resultsJson = JArray.Parse(jsonResult).ToList();

            var tableView = new TableView<JToken>
            {
                Items = resultsJson,
            };

            tableView.AddColumn(
                cellValue: f => f["id"],
                header: new ContentView("Version"));

            tableView.AddColumn(
                cellValue: f => f["script"],
                header: new ContentView("Script"));

            var consoleRenderer = new ConsoleRenderer(
                invocationContext.Console,
                mode: invocationContext.BindingContext.OutputMode(),
                resetAfterRender: true);

            var screen = new ScreenView(renderer: consoleRenderer) { Child = tableView };
            screen.Render(region);
        }
    }
}
