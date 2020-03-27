// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.CommandLine.Invocation;
using System.CommandLine.Rendering;
using System.CommandLine.Rendering.Views;
using System.Net.Http;
using System.Threading.Tasks;
using FhirSchemaManager.Model;
using Newtonsoft.Json;

namespace FhirSchemaManager.Commands
{
    public static class CurrentCommand
    {
        public static async Task HandlerAsync(InvocationContext invocationContext, Uri fhirServer)
        {
            Console.WriteLine($"--fhir-server {fhirServer}");

            var region = new Region(
                0,
                0,
                Console.WindowWidth,
                Console.WindowHeight,
                true);

            List<CurrentVersion> currentVersions = await GetCurrentVersionInformation(fhirServer);

            var tableView = new TableView<CurrentVersion>
            {
                Items = currentVersions,
            };

            tableView.AddColumn(
               cellValue: currentVersion => currentVersion.Id,
               header: new ContentView("Version"));

            tableView.AddColumn(
                cellValue: currentVersion => currentVersion.Status,
                header: new ContentView("Status"));

            tableView.AddColumn(
                cellValue: currentVersion => currentVersion.Servers,
                header: new ContentView("Servers"));

            var consoleRenderer = new ConsoleRenderer(
                invocationContext.Console,
                mode: invocationContext.BindingContext.OutputMode(),
                resetAfterRender: true);

            var screen = new ScreenView(renderer: consoleRenderer) { Child = tableView };
            screen.Render(region);
        }

        private static async Task<List<CurrentVersion>> GetCurrentVersionInformation(Uri serverUri)
        {
            List<CurrentVersion> currentVersionList = null;

            var httpClient = new HttpClient
            {
                BaseAddress = serverUri,
            };

            using (var response = await httpClient.GetAsync(new Uri("/_schema/compatibility", UriKind.Relative)))
            {
                var responseBodyAsString = await response.Content.ReadAsStringAsync();
                currentVersionList = JsonConvert.DeserializeObject<List<CurrentVersion>>(responseBodyAsString);
            }

            return currentVersionList;
        }
    }
}
