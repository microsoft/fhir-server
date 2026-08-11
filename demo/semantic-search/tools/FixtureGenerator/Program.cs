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
const string ControlPatientId = "c27d6972-16be-4a01-8b9c-0d994c58d9bc";
const string SyntheticTagSystem = "https://example.org/fhir/CodeSystem/demo-data";
const string DocumentIdentifierSystem = "https://example.org/fhir/NamingSystem/semantic-search-demo-document-id";
const string RadiologistReference = "Practitioner/demo-practitioner-radiology";
const string RadiologistDisplay = "Leah Morgan, MD";
var jsonSerializerOptions = new JsonSerializerOptions { WriteIndented = true };
string[] unknownArguments = args.Where(argument => argument.StartsWith("--", StringComparison.Ordinal) && !string.Equals(argument, "--regenerate-pdf", StringComparison.Ordinal)).ToArray();
if (unknownArguments.Length > 0)
{
    throw new ArgumentException($"Unknown argument: {unknownArguments[0]}", nameof(args));
}

bool regeneratePdf = args.Contains("--regenerate-pdf", StringComparer.Ordinal);
string[] pathArguments = args.Where(argument => !argument.StartsWith("--", StringComparison.Ordinal)).ToArray();
if (pathArguments.Length > 1)
{
    throw new ArgumentException("Pass at most one demo directory.", nameof(args));
}

string demoDirectory = pathArguments.Length == 1
    ? Path.GetFullPath(pathArguments[0])
    : FindDemoDirectory(AppContext.BaseDirectory);
string sourceDirectory = Path.Combine(demoDirectory, "resources", "source-documents");
string binaryDirectory = Path.Combine(demoDirectory, "resources", "binaries");
string documentReferenceDirectory = Path.Combine(demoDirectory, "resources", "document-references");
string diagnosticReportDirectory = Path.Combine(demoDirectory, "resources", "diagnostic-reports");
string radiologyManifestPath = Path.Combine(demoDirectory, "resources", "radiology-fixture-manifest.json");

string textSourcePath = Path.Combine(sourceDirectory, "demo-long-vestibular-note.txt");
string pdfPageOnePath = Path.Combine(sourceDirectory, "demo-long-autonomic-consult-page-1.txt");
string pdfPageTwoPath = Path.Combine(sourceDirectory, "demo-long-autonomic-consult-page-2.txt");
string pdfOutputPath = Path.Combine(sourceDirectory, "demo-long-autonomic-consult.pdf");

byte[] textBytes = Encoding.UTF8.GetBytes(File.ReadAllText(textSourcePath, Encoding.UTF8));
byte[] pdfBytes;
if (regeneratePdf || !File.Exists(pdfOutputPath))
{
    pdfBytes = BuildPdf(
        File.ReadAllText(pdfPageOnePath, Encoding.UTF8),
        File.ReadAllText(pdfPageTwoPath, Encoding.UTF8));
    File.WriteAllBytes(pdfOutputPath, pdfBytes);
}
else
{
    pdfBytes = File.ReadAllBytes(pdfOutputPath);
}

VerifyPdf(pdfBytes);

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

using JsonDocument radiologyManifest = JsonDocument.Parse(File.ReadAllText(radiologyManifestPath, Encoding.UTF8));
JsonElement radiologyManifestRoot = radiologyManifest.RootElement;
ValidateManifestPatient(radiologyManifestRoot, "primaryPatient", $"Patient/{PatientId}");
ValidateManifestPatient(radiologyManifestRoot, "controlPatient", $"Patient/{ControlPatientId}");

int radiologyStudyCount = 0;
foreach (JsonElement study in radiologyManifestRoot.GetProperty("studies").EnumerateArray())
{
    string patientId = GetRequiredString(study, "patientId");
    string patientDisplay = GetRequiredString(study, "patientDisplay");
    string sourceFile = GetSafeFileName(study, "sourceFile");
    string sourcePath = Path.Combine(sourceDirectory, sourceFile);
    string sourceText = File.ReadAllText(sourcePath, Encoding.UTF8);
    byte[] sourceBytes = Encoding.UTF8.GetBytes(sourceText);

    ValidateExpectedPhrases(study, sourceText);

    string binaryId = GetRequiredString(study, "binaryId");
    string documentReferenceId = GetRequiredString(study, "documentReferenceId");
    string diagnosticReportId = GetRequiredString(study, "diagnosticReportId");
    string issued = GetRequiredString(study, "issued");
    string title = GetRequiredString(study, "title");

    WriteJson(
        Path.Combine(binaryDirectory, $"{binaryId}.json"),
        CreateBinary(binaryId, "text/plain; charset=utf-8", sourceBytes, patientId),
        jsonSerializerOptions);
    WriteJson(
        Path.Combine(documentReferenceDirectory, $"{documentReferenceId}.json"),
        CreateRadiologyDocumentReference(study, sourceBytes),
        jsonSerializerOptions);
    WriteJson(
        Path.Combine(diagnosticReportDirectory, $"{diagnosticReportId}.json"),
        CreateRadiologyDiagnosticReport(study, sourceBytes),
        jsonSerializerOptions);

    ValidateGeneratedRadiologyStudy(
        study,
        sourceBytes,
        Path.Combine(binaryDirectory, $"{binaryId}.json"),
        Path.Combine(documentReferenceDirectory, $"{documentReferenceId}.json"),
        Path.Combine(diagnosticReportDirectory, $"{diagnosticReportId}.json"));

    Console.WriteLine($"Radiology fixture {GetRequiredString(study, "key")}: {patientDisplay}, {issued}, {sourceBytes.Length} bytes");
    radiologyStudyCount++;
}

ValidateRadiologyManifestReferences(radiologyManifestRoot, demoDirectory);
ValidatePrimaryStudyChronology(radiologyManifestRoot);
ValidateAllJsonResources(Path.Combine(demoDirectory, "resources"));

Console.WriteLine($"Text fixture: {textBytes.Length} bytes, {CountWords(Encoding.UTF8.GetString(textBytes))} words");
Console.WriteLine($"PDF fixture: {pdfBytes.Length} bytes, 2 pages, {CountWords(File.ReadAllText(pdfPageOnePath) + " " + File.ReadAllText(pdfPageTwoPath))} source words");
Console.WriteLine($"Radiology fixtures: {radiologyStudyCount} studies validated from {Path.GetFileName(radiologyManifestPath)}");
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

static object CreateBinary(string id, string contentType, byte[] data, string patientId = PatientId)
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
        securityContext = new { reference = $"Patient/{patientId}" },
        data = Convert.ToBase64String(data),
    };
}

static object CreateRadiologyDocumentReference(JsonElement study, byte[] data)
{
    string patientId = GetRequiredString(study, "patientId");
    string patientDisplay = GetRequiredString(study, "patientDisplay");
    string diagnosticReportId = GetRequiredString(study, "diagnosticReportId");
    string binaryId = GetRequiredString(study, "binaryId");
    string effectiveDateTime = GetRequiredString(study, "effectiveDateTime");
    string issued = GetRequiredString(study, "issued");

    return new
    {
        resourceType = "DocumentReference",
        id = GetRequiredString(study, "documentReferenceId"),
        meta = new
        {
            tag = new[] { new { system = SyntheticTagSystem, code = "synthetic" } },
        },
        masterIdentifier = new
        {
            system = DocumentIdentifierSystem,
            value = GetRequiredString(study, "masterIdentifier"),
        },
        status = "current",
        docStatus = "final",
        type = new
        {
            coding = new[] { new { system = "http://loinc.org", code = "18748-4", display = "Diagnostic imaging study" } },
            text = "Radiology report",
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
        subject = new { reference = $"Patient/{patientId}", display = patientDisplay },
        date = issued,
        author = new[] { new { reference = RadiologistReference, display = RadiologistDisplay } },
        description = GetRequiredString(study, "description"),
        content = new[]
        {
            new
            {
                attachment = CreateAttachment("text/plain; charset=utf-8", binaryId, GetRequiredString(study, "title"), issued, data),
            },
        },
        context = new
        {
            period = new { start = effectiveDateTime, end = issued },
            practiceSetting = new { text = "Radiology" },
            related = new[] { new { reference = $"DiagnosticReport/{diagnosticReportId}" } },
        },
    };
}

static object CreateRadiologyDiagnosticReport(JsonElement study, byte[] data)
{
    string patientId = GetRequiredString(study, "patientId");
    string patientDisplay = GetRequiredString(study, "patientDisplay");
    string binaryId = GetRequiredString(study, "binaryId");
    string issued = GetRequiredString(study, "issued");

    return new
    {
        resourceType = "DiagnosticReport",
        id = GetRequiredString(study, "diagnosticReportId"),
        meta = new
        {
            tag = new[] { new { system = SyntheticTagSystem, code = "synthetic" } },
        },
        status = "final",
        category = new[]
        {
            new
            {
                coding = new[]
                {
                    new
                    {
                        system = "http://terminology.hl7.org/CodeSystem/v2-0074",
                        code = "RAD",
                        display = "Radiology",
                    },
                },
            },
        },
        code = new
        {
            coding = new[]
            {
                new
                {
                    system = "http://loinc.org",
                    code = GetRequiredString(study, "code"),
                    display = GetRequiredString(study, "codeDisplay"),
                },
            },
            text = "CT chest without intravenous contrast",
        },
        subject = new { reference = $"Patient/{patientId}", display = patientDisplay },
        effectiveDateTime = GetRequiredString(study, "effectiveDateTime"),
        issued,
        performer = new[] { new { reference = RadiologistReference, display = RadiologistDisplay } },
        conclusion = GetRequiredString(study, "conclusion"),
        presentedForm = new[]
        {
            CreateAttachment("text/plain; charset=utf-8", binaryId, GetRequiredString(study, "title"), issued, data),
        },
    };
}

static object CreateAttachment(string contentType, string binaryId, string title, string creation, byte[] data)
{
#pragma warning disable CA5350 // FHIR R4 Attachment.hash requires SHA-1.
    string hash = Convert.ToBase64String(SHA1.HashData(data));
#pragma warning restore CA5350

    return new
    {
        contentType,
        language = "en-US",
        size = data.Length,
        hash,
        url = $"Binary/{binaryId}",
        title,
        creation,
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

static void ValidateManifestPatient(JsonElement manifest, string propertyName, string expectedReference)
{
    string actualReference = GetRequiredString(manifest, propertyName);
    if (!string.Equals(actualReference, expectedReference, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Manifest {propertyName} must be {expectedReference}, but was {actualReference}.");
    }
}

static void ValidateExpectedPhrases(JsonElement study, string sourceText)
{
    foreach (JsonElement phraseElement in study.GetProperty("expectedPhrases").EnumerateArray())
    {
        string phrase = phraseElement.GetString() ?? throw new InvalidOperationException("Expected phrase cannot be null.");
        if (!sourceText.Contains(phrase, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Radiology source {GetRequiredString(study, "sourceFile")} is missing expected phrase: {phrase}");
        }
    }
}

static void ValidateGeneratedRadiologyStudy(
    JsonElement study,
    byte[] sourceBytes,
    string binaryPath,
    string documentReferencePath,
    string diagnosticReportPath)
{
    string patientReference = $"Patient/{GetRequiredString(study, "patientId")}";
    string binaryReference = $"Binary/{GetRequiredString(study, "binaryId")}";
    string diagnosticReportReference = $"DiagnosticReport/{GetRequiredString(study, "diagnosticReportId")}";

    using JsonDocument binary = JsonDocument.Parse(File.ReadAllText(binaryPath, Encoding.UTF8));
    using JsonDocument documentReference = JsonDocument.Parse(File.ReadAllText(documentReferencePath, Encoding.UTF8));
    using JsonDocument diagnosticReport = JsonDocument.Parse(File.ReadAllText(diagnosticReportPath, Encoding.UTF8));

    AssertJsonString(binary.RootElement.GetProperty("securityContext"), "reference", patientReference, binaryPath);
    byte[] generatedBytes = Convert.FromBase64String(GetRequiredString(binary.RootElement, "data"));
    if (!generatedBytes.AsSpan().SequenceEqual(sourceBytes))
    {
        throw new InvalidOperationException($"Generated Binary bytes do not match source: {binaryPath}");
    }

    AssertJsonString(documentReference.RootElement.GetProperty("subject"), "reference", patientReference, documentReferencePath);
    JsonElement documentAttachment = documentReference.RootElement.GetProperty("content")[0].GetProperty("attachment");
    ValidateAttachment(documentAttachment, binaryReference, sourceBytes, documentReferencePath);
    AssertJsonString(documentReference.RootElement.GetProperty("context").GetProperty("related")[0], "reference", diagnosticReportReference, documentReferencePath);

    AssertJsonString(diagnosticReport.RootElement.GetProperty("subject"), "reference", patientReference, diagnosticReportPath);
    AssertJsonString(diagnosticReport.RootElement, "conclusion", GetRequiredString(study, "conclusion"), diagnosticReportPath);
    ValidateAttachment(diagnosticReport.RootElement.GetProperty("presentedForm")[0], binaryReference, sourceBytes, diagnosticReportPath);
}

static void ValidateAttachment(JsonElement attachment, string binaryReference, byte[] sourceBytes, string resourcePath)
{
    AssertJsonString(attachment, "url", binaryReference, resourcePath);
    if (attachment.GetProperty("size").GetInt32() != sourceBytes.Length)
    {
        throw new InvalidOperationException($"Attachment size does not match source bytes: {resourcePath}");
    }

#pragma warning disable CA5350 // FHIR R4 Attachment.hash requires SHA-1.
    string expectedHash = Convert.ToBase64String(SHA1.HashData(sourceBytes));
#pragma warning restore CA5350
    AssertJsonString(attachment, "hash", expectedHash, resourcePath);
}

static void ValidateRadiologyManifestReferences(JsonElement manifest, string demoDirectory)
{
    string primaryPatientReference = GetRequiredString(manifest, "primaryPatient");
    string controlPatientReference = GetRequiredString(manifest, "controlPatient");

    foreach (JsonElement distractor in manifest.GetProperty("samePatientDistractors").EnumerateArray())
    {
        ValidateResourceReferencePatient(distractor.GetString(), demoDirectory, primaryPatientReference);
    }

    foreach (JsonElement query in manifest.GetProperty("semanticQueries").EnumerateArray())
    {
        _ = GetRequiredString(query, "question");
        foreach (JsonElement reference in query.GetProperty("expectedPrimaryResources").EnumerateArray())
        {
            ValidateResourceReferencePatient(reference.GetString(), demoDirectory, primaryPatientReference);
        }

        foreach (JsonElement reference in query.GetProperty("excludedResources").EnumerateArray())
        {
            ValidateResourceReferencePatient(reference.GetString(), demoDirectory, controlPatientReference);
        }
    }
}

static void ValidateResourceReferencePatient(string? reference, string demoDirectory, string expectedPatientReference)
{
    if (string.IsNullOrWhiteSpace(reference))
    {
        throw new InvalidOperationException("Manifest resource reference cannot be empty.");
    }

    string[] segments = reference.Split('/');
    if (segments.Length != 2 || segments.Any(string.IsNullOrWhiteSpace))
    {
        throw new InvalidOperationException($"Invalid manifest resource reference: {reference}");
    }

    string directory = segments[0] switch
    {
        "DiagnosticReport" => "diagnostic-reports",
        "DocumentReference" => "document-references",
        _ => throw new InvalidOperationException($"Unsupported manifest resource type: {segments[0]}"),
    };

    string path = Path.Combine(demoDirectory, "resources", directory, $"{segments[1]}.json");
    if (!File.Exists(path))
    {
        throw new InvalidOperationException($"Manifest resource does not exist: {reference}");
    }

    using JsonDocument resource = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
    AssertJsonString(resource.RootElement.GetProperty("subject"), "reference", expectedPatientReference, path);
}

static void ValidatePrimaryStudyChronology(JsonElement manifest)
{
    DateTimeOffset? previousDate = null;
    int primaryStudyCount = 0;
    int controlStudyCount = 0;
    var resourceReferences = new HashSet<string>(StringComparer.Ordinal);
    foreach (JsonElement study in manifest.GetProperty("studies").EnumerateArray())
    {
        foreach (string propertyName in new[] { "binaryId", "documentReferenceId", "diagnosticReportId" })
        {
            string resourceId = GetRequiredString(study, propertyName);
            if (!resourceReferences.Add(resourceId))
            {
                throw new InvalidOperationException($"Radiology study resource ids must be unique: {resourceId}");
            }
        }

        string patientId = GetRequiredString(study, "patientId");
        if (string.Equals(patientId, ControlPatientId, StringComparison.Ordinal))
        {
            controlStudyCount++;
            continue;
        }

        if (!string.Equals(patientId, PatientId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Radiology study has an unexpected patient id: {patientId}");
        }

        DateTimeOffset currentDate = DateTimeOffset.Parse(GetRequiredString(study, "effectiveDateTime"), System.Globalization.CultureInfo.InvariantCulture);
        if (previousDate.HasValue && currentDate <= previousDate.Value)
        {
            throw new InvalidOperationException("Primary radiology studies must be listed in strictly increasing chronological order.");
        }

        previousDate = currentDate;
        primaryStudyCount++;
    }

    if (primaryStudyCount < 3)
    {
        throw new InvalidOperationException("The radiology scenario requires at least three primary-patient studies.");
    }

    if (controlStudyCount < 1)
    {
        throw new InvalidOperationException("The radiology scenario requires at least one control-patient study.");
    }
}

static void ValidateAllJsonResources(string resourceDirectory)
{
    foreach (string path in Directory.EnumerateFiles(resourceDirectory, "*.json", SearchOption.AllDirectories))
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"FHIR fixture must contain a JSON object: {path}");
        }
    }
}

static string GetRequiredString(JsonElement element, string propertyName)
{
    if (!element.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind != JsonValueKind.String)
    {
        throw new InvalidOperationException($"Required string property is missing: {propertyName}");
    }

    string? result = value.GetString();
    return string.IsNullOrWhiteSpace(result)
        ? throw new InvalidOperationException($"Required string property is empty: {propertyName}")
        : result;
}

static string GetSafeFileName(JsonElement element, string propertyName)
{
    string fileName = GetRequiredString(element, propertyName);
    if (!string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal) || !fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"Manifest source file must be a plain .txt file name: {fileName}");
    }

    return fileName;
}

static void AssertJsonString(JsonElement element, string propertyName, string expected, string resourcePath)
{
    string actual = GetRequiredString(element, propertyName);
    if (!string.Equals(actual, expected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Expected {propertyName} '{expected}' but found '{actual}' in {resourcePath}.");
    }
}

static int CountWords(string text)
{
    return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
