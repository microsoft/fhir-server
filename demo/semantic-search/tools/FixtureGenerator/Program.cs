// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

const string PatientId = "8f789d0b-3145-4cf2-8504-13159edaa747";
const string SyntheticTagSystem = "https://example.org/fhir/CodeSystem/demo-data";
const string DocumentIdentifierSystem = "https://example.org/fhir/NamingSystem/semantic-search-demo-document-id";
var jsonSerializerOptions = new JsonSerializerOptions { WriteIndented = true };

string demoDirectory = args.Length == 1
    ? Path.GetFullPath(args[0])
    : FindDemoDirectory(AppContext.BaseDirectory);
string sourceDirectory = Path.Combine(demoDirectory, "resources", "source-documents");
string binaryDirectory = Path.Combine(demoDirectory, "resources", "binaries");
string documentReferenceDirectory = Path.Combine(demoDirectory, "resources", "document-references");

string textSourcePath = Path.Combine(sourceDirectory, "demo-long-vestibular-note.txt");
string pdfPageOnePath = Path.Combine(sourceDirectory, "demo-long-autonomic-consult-page-1.txt");
string pdfPageTwoPath = Path.Combine(sourceDirectory, "demo-long-autonomic-consult-page-2.txt");
string pdfOutputPath = Path.Combine(sourceDirectory, "demo-long-autonomic-consult.pdf");

byte[] textBytes = Encoding.UTF8.GetBytes(File.ReadAllText(textSourcePath, Encoding.UTF8));
byte[] pdfBytes = BuildPdf(
    File.ReadAllText(pdfPageOnePath, Encoding.UTF8),
    File.ReadAllText(pdfPageTwoPath, Encoding.UTF8));

VerifyPdf(pdfBytes);
File.WriteAllBytes(pdfOutputPath, pdfBytes);

WriteJson(
    Path.Combine(binaryDirectory, "demo-binary-long-vestibular-text.json"),
    CreateBinary("demo-binary-long-vestibular-text", "text/plain; charset=utf-8", textBytes),
    jsonSerializerOptions);
WriteJson(
    Path.Combine(binaryDirectory, "demo-binary-long-autonomic-pdf.json"),
    CreateBinary("demo-binary-long-autonomic-pdf", "application/pdf", pdfBytes),
    jsonSerializerOptions);
WriteJson(
    Path.Combine(documentReferenceDirectory, "demo-doc-long-vestibular-text.json"),
    CreateDocumentReference(
        id: "demo-doc-long-vestibular-text",
        masterIdentifier: "DOC-20260303-NEURO-0915",
        typeCode: "11488-4",
        typeDisplay: "Consultation note",
        typeText: "Vestibular neurology follow-up note",
        date: "2026-03-03T16:30:00Z",
        authorReference: "Practitioner/demo-practitioner-neurology",
        authorDisplay: "Sofia Alvarez, MD",
        description: "Long text note distinguishing positional vertigo from orthostatic presyncope.",
        contentType: "text/plain; charset=utf-8",
        binaryId: "demo-binary-long-vestibular-text",
        title: "Vestibular neurology follow-up note - 2026-03-03",
        data: textBytes),
    jsonSerializerOptions);
WriteJson(
    Path.Combine(documentReferenceDirectory, "demo-doc-long-autonomic-pdf.json"),
    CreateDocumentReference(
        id: "demo-doc-long-autonomic-pdf",
        masterIdentifier: "DOC-20260318-AUTO-1430",
        typeCode: "11488-4",
        typeDisplay: "Consultation note",
        typeText: "Autonomic and syncope clinic consultation",
        date: "2026-03-18T19:15:00Z",
        authorReference: "Practitioner/demo-practitioner-neurology",
        authorDisplay: "Sofia Alvarez, MD",
        description: "Two-page PDF consultation distinguishing orthostatic presyncope from positional vertigo.",
        contentType: "application/pdf",
        binaryId: "demo-binary-long-autonomic-pdf",
        title: "Autonomic and syncope clinic consultation - 2026-03-18",
        data: pdfBytes),
    jsonSerializerOptions);

Console.WriteLine($"Text fixture: {textBytes.Length} bytes, {CountWords(Encoding.UTF8.GetString(textBytes))} words");
Console.WriteLine($"PDF fixture: {pdfBytes.Length} bytes, 2 pages, {CountWords(File.ReadAllText(pdfPageOnePath) + " " + File.ReadAllText(pdfPageTwoPath))} source words");
Console.WriteLine($"Generated resources under {Path.Combine(demoDirectory, "resources")}");

static string FindDemoDirectory(string startDirectory)
{
    DirectoryInfo? directory = new DirectoryInfo(startDirectory);
    while (directory != null)
    {
        string candidate = Path.Combine(directory.FullName, "demo", "semantic-search");
        if (Directory.Exists(candidate))
        {
            return candidate;
        }

        directory = directory.Parent;
    }

    throw new InvalidOperationException("Could not locate demo/semantic-search. Pass its path as the first argument.");
}

static byte[] BuildPdf(params string[] pageTexts)
{
    using var builder = new PdfDocumentBuilder();
    PdfDocumentBuilder.AddedFont font = builder.AddStandard14Font(Standard14Font.Helvetica);

    foreach (string pageText in pageTexts)
    {
        PdfPageBuilder page = builder.AddPage(PageSize.Letter);
        int y = 742;

        foreach (string line in WrapText(pageText, 120))
        {
            if (y < 42)
            {
                throw new InvalidOperationException("A source page exceeds the single-page PDF layout budget.");
            }

            page.AddText(line, 7, new PdfPoint(42, y), font);
            y -= 9;
        }
    }

    return builder.Build();
}

static IEnumerable<string> WrapText(string text, int maximumLineLength)
{
    foreach (string paragraph in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
    {
        if (string.IsNullOrWhiteSpace(paragraph))
        {
            yield return string.Empty;
            continue;
        }

        var line = new StringBuilder();
        foreach (string word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > maximumLineLength)
            {
                yield return line.ToString();
                line.Clear();
            }

            if (line.Length > 0)
            {
                line.Append(' ');
            }

            line.Append(word);
        }

        if (line.Length > 0)
        {
            yield return line.ToString();
        }
    }
}

static void VerifyPdf(byte[] pdfBytes)
{
    using PdfDocument document = PdfDocument.Open(pdfBytes);
    if (document.NumberOfPages != 2)
    {
        throw new InvalidOperationException($"Expected 2 PDF pages but generated {document.NumberOfPages}.");
    }

    string pageOne = ContentOrderTextExtractor.GetText(document.GetPage(1));
    string pageTwo = ContentOrderTextExtractor.GetText(document.GetPage(2));
    if (!pageOne.Contains("abdominal binder", StringComparison.OrdinalIgnoreCase) ||
        !pageTwo.Contains("hot shower", StringComparison.OrdinalIgnoreCase) ||
        !pageTwo.Contains("Hydrochlorothiazide will be stopped", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Generated PDF did not preserve the expected page-specific test phrases.");
    }
}

static object CreateBinary(string id, string contentType, byte[] data)
{
    return new
    {
        resourceType = "Binary",
        id,
        meta = new
        {
            tag = new[] { new { system = SyntheticTagSystem, code = "synthetic" } },
        },
        contentType,
        securityContext = new { reference = $"Patient/{PatientId}" },
        data = Convert.ToBase64String(data),
    };
}

static object CreateDocumentReference(
    string id,
    string masterIdentifier,
    string typeCode,
    string typeDisplay,
    string typeText,
    string date,
    string authorReference,
    string authorDisplay,
    string description,
    string contentType,
    string binaryId,
    string title,
    byte[] data)
{
#pragma warning disable CA5350 // FHIR R4 Attachment.hash requires SHA-1.
    string hash = Convert.ToBase64String(SHA1.HashData(data));
#pragma warning restore CA5350

    return new
    {
        resourceType = "DocumentReference",
        id,
        meta = new
        {
            tag = new[] { new { system = SyntheticTagSystem, code = "synthetic" } },
        },
        masterIdentifier = new { system = DocumentIdentifierSystem, value = masterIdentifier },
        status = "current",
        docStatus = "final",
        type = new
        {
            coding = new[] { new { system = "http://loinc.org", code = typeCode, display = typeDisplay } },
            text = typeText,
        },
        category = new[]
        {
            new
            {
                coding = new[]
                {
                    new
                    {
                        system = "http://hl7.org/fhir/us/core/CodeSystem/us-core-documentreference-category",
                        code = "clinical-note",
                        display = "Clinical Note",
                    },
                },
            },
        },
        subject = new { reference = $"Patient/{PatientId}", display = "Elena Marquez" },
        date,
        author = new[] { new { reference = authorReference, display = authorDisplay } },
        description,
        content = new[]
        {
            new
            {
                attachment = new
                {
                    contentType,
                    language = "en-US",
                    size = data.Length,
                    hash,
                    url = $"Binary/{binaryId}",
                    title,
                    creation = date,
                },
            },
        },
    };
}

static void WriteJson(string path, object value, JsonSerializerOptions options)
{
    File.WriteAllText(path, JsonSerializer.Serialize(value, options) + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
}

static int CountWords(string text)
{
    return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
