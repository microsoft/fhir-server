using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Newtonsoft.Json;
using RedoxExperiment;
using Xunit;
using Stj = System.Text.Json;

return Run(args);

static int Run(string[] args)
{
    if (args.Length < 2 || args[0] is not ("verify" or "benchmark"))
    {
        Console.Error.WriteLine("Usage: RedoxExperiment verify <corpus.gz> | benchmark <corpus.gz> <baseline|redox> <parse|roundtrip|metadata> <passes>");
        return 2;
    }

    using var file = File.OpenRead(args[1]);
    using var gzip = new GZipStream(file, CompressionMode.Decompress);
    using var text = new StreamReader(gzip);
    var corpusText = text.ReadToEnd();
    var corpus = corpusText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
#pragma warning disable CS0618
    var parser = new FhirJsonParser(new ParserSettings
    {
        PermissiveParsing = true,
        TruncateDateTimeToDate = true,
    });
#pragma warning restore CS0618
    var serializer = new FhirJsonSerializer();

    if (args[0] == "verify")
    {
        VerifyReader();
        VerifyFhir(parser, serializer);
        VerifyMetadata();
        var matching = 0;
        foreach (var json in corpus)
        {
            var expected = serializer.SerializeToString(Parse(parser, json, "baseline"));
            var actual = serializer.SerializeToString(Parse(parser, json, "redox"));
            Assert.Equal(expected, actual);
            Assert.True(Newtonsoft.Json.Linq.JToken.DeepEquals(
                Newtonsoft.Json.Linq.JToken.Parse(Encoding.UTF8.GetString(MetadataRewrite.Rewrite(json, "baseline"))),
                Newtonsoft.Json.Linq.JToken.Parse(Encoding.UTF8.GetString(MetadataRewrite.Rewrite(json, "redox")))));
            matching++;
        }

        var malformed = new[]
        {
            """{"resourceType":"Patient",}""",
            """{/*comment*/"resourceType":"Patient"}""",
            """{'resourceType':'Patient'}""",
            """{"resourceType":"Patient","active":true,"active":false}""",
            """{"resourceType":"Patient","active":tru}""",
            """{"resourceType":"Patient"} trailing""",
            """{"resourceType":"Patient","unknownField":3}""",
        };
        var compatibility = malformed.Select(json => new
        {
            json,
            baseline = Outcome(parser, json, "baseline"),
            redox = Outcome(parser, json, "redox"),
        }).ToArray();
        var compatibilityMismatches = compatibility.Count(item =>
            item.baseline.StartsWith("accepted", StringComparison.Ordinal) !=
            item.redox.StartsWith("accepted", StringComparison.Ordinal));
        Write(new
        {
            kind = "verification",
            tokenContract = "passed",
            literalFhirCases = "passed",
            metadataCases = "passed",
            corpusMatching = matching,
            compatibilityMismatches,
            readyForIntegration = compatibilityMismatches == 0,
            compatibility,
        });
        return compatibilityMismatches == 0 ? 0 : 3;
    }

    if (args.Length != 5 || args[2] is not ("baseline" or "redox") ||
        args[3] is not ("parse" or "roundtrip" or "metadata") ||
        !int.TryParse(args[4], out var passes) || passes <= 0)
    {
        Console.Error.WriteLine("Invalid benchmark arguments.");
        return 2;
    }

    var variant = args[2];
    var workload = args[3];
    long checksum = 0;
    Action pass = () =>
    {
        foreach (var json in corpus)
        {
            if (workload == "metadata")
            {
                checksum += MetadataRewrite.Rewrite(json, variant).Length;
            }
            else
            {
                var resource = Parse(parser, json, variant);
                checksum += workload == "roundtrip"
                    ? serializer.SerializeToString(resource).Length
                    : resource.Id?.Length ?? 0;
            }
        }
    };

    var warmup = Stopwatch.StartNew();
    do
    {
        pass();
    }
    while (warmup.Elapsed < TimeSpan.FromSeconds(3));

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    using var process = Process.GetCurrentProcess();
    var collections = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
    var allocated = GC.GetTotalAllocatedBytes(true);
    var cpu = process.TotalProcessorTime;
    var timer = Stopwatch.StartNew();
    for (var i = 0; i < passes; i++)
    {
        pass();
    }

    timer.Stop();
    var cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds;
    var allocatedBytes = GC.GetTotalAllocatedBytes(true) - allocated;
    process.Refresh();
    Write(new
    {
        kind = "component-measurement",
        variant,
        workload,
        operations = (long)passes * corpus.Length,
        elapsedMs = timer.Elapsed.TotalMilliseconds,
        cpuMs,
        allocatedBytes,
        gc = Enumerable.Range(0, 3).Select(i => GC.CollectionCount(i) - collections[i]).ToArray(),
        workingSetBytes = process.WorkingSet64,
        peakWorkingSetBytes = process.PeakWorkingSet64,
        privateBytes = process.PrivateMemorySize64,
        heapBytes = GC.GetGCMemoryInfo().HeapSizeBytes,
        checksum,
        corpusSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(corpusText))),
        corpusCount = corpus.Length,
        runtime = RuntimeInformation.FrameworkDescription,
        processorCount = Environment.ProcessorCount,
        serverGc = System.Runtime.GCSettings.IsServerGC,
        redoxVersion = typeof(REDox.DElement).Assembly.GetName().Version?.ToString(),
    });
    return 0;
}

static Resource Parse(FhirJsonParser parser, string json, string variant)
{
    using JsonReader reader = variant == "baseline"
        ? new JsonTextReader(new StringReader(json))
        {
            DateParseHandling = DateParseHandling.None,
            FloatParseHandling = FloatParseHandling.Decimal,
            CloseInput = true,
        }
        : new RedoxJsonReader(json);
    return parser.Parse<Resource>(reader);
}

static string Outcome(FhirJsonParser parser, string json, string variant)
{
    try
    {
        return Parse(parser, json, variant) is null ? "null" : "accepted";
    }
    catch (Exception exception)
    {
        return $"rejected:{exception.GetType().Name}";
    }
}

static void VerifyReader()
{
    const string json = """{"text":"a\u00e9","value":1.2300,"array":[null,true,42]}""";
    using var reader = new RedoxJsonReader(json);
    var tokens = new List<JsonToken>();
    var values = new List<object?>();
    while (reader.Read())
    {
        tokens.Add(reader.TokenType);
        values.Add(reader.Value);
    }

    Assert.Equal(
        new[] { JsonToken.StartObject, JsonToken.PropertyName, JsonToken.String,
            JsonToken.PropertyName, JsonToken.Float, JsonToken.PropertyName,
            JsonToken.StartArray, JsonToken.Null, JsonToken.Boolean, JsonToken.Integer,
            JsonToken.EndArray, JsonToken.EndObject },
        tokens);
    Assert.Equal("a\u00e9", values[2]);
    Assert.Equal(1.2300m, values[4]);
    Assert.Equal(4, (decimal.GetBits(Assert.IsType<decimal>(values[4]))[3] >> 16) & 0xff);
    Assert.Equal(42L, values[9]);
}

static void VerifyFhir(FhirJsonParser parser, FhirJsonSerializer serializer)
{
    const string patientJson = """
        {"id":"p1","name":[{"given":["a\u00e9",null],"_given":[null,{"extension":[{"url":"http://example.org/test","valueString":"missing"}]}]}],
        "birthDate":"1970-03","deceasedBoolean":false,"resourceType":"Patient"}
        """;
    var patient = Assert.IsType<Patient>(Parse(parser, patientJson, "redox"));
    Assert.Equal("p1", patient.Id);
    Assert.Equal("1970-03", patient.BirthDate);
    Assert.Equal("a\u00e9", patient.Name[0].GivenElement[0].Value);
    Assert.Null(patient.Name[0].GivenElement[1].Value);
    Assert.Equal("missing", Assert.IsType<FhirString>(patient.Name[0].GivenElement[1].Extension[0].Value).Value);
    Assert.False(Assert.IsType<FhirBoolean>(patient.Deceased).Value);
    Assert.Equal(serializer.SerializeToString(Parse(parser, patientJson, "baseline")), serializer.SerializeToString(patient));

    const string observationJson = """
        {"resourceType":"Observation","id":"o1","status":"final","code":{"text":"test"},
        "valueQuantity":{"value":1.2300,"unit":"mg"},"effectiveDateTime":"2020-01-02T03:04:05.123+05:30"}
        """;
    var observation = Assert.IsType<Observation>(Parse(parser, observationJson, "redox"));
    var value = Assert.IsType<Quantity>(observation.Value).Value!.Value;
    Assert.Equal(1.2300m, value);
    Assert.Equal(4, (decimal.GetBits(value)[3] >> 16) & 0xff);
    Assert.Equal("2020-01-02T03:04:05.123+05:30", Assert.IsType<FhirDateTime>(observation.Effective).Value);
    Assert.Equal(serializer.SerializeToString(Parse(parser, observationJson, "baseline")), serializer.SerializeToString(observation));
}

static void VerifyMetadata()
{
    var cases = new[]
    {
        """{"resourceType":"Patient","id":"p1"}""",
        """{"resourceType":"Patient","meta":{"versionId":"1","lastUpdated":"2000-01-01T00:00:00Z","tag":[{"code":"keep"}]},"id":"p1"}""",
        """{"resourceType":"Observation","id":"o1","valueQuantity":{"value":1.2300}}""",
    };
    foreach (var json in cases)
    {
        var actual = MetadataRewrite.Rewrite(json, "redox");
        using var document = Stj.JsonDocument.Parse(actual);
        Assert.Equal("2", document.RootElement.GetProperty("meta").GetProperty("versionId").GetString());
        Assert.Equal("2026-01-02T03:04:05.123Z", document.RootElement.GetProperty("meta").GetProperty("lastUpdated").GetString());
        if (document.RootElement.TryGetProperty("valueQuantity", out var quantity))
        {
            Assert.Equal("1.2300", quantity.GetProperty("value").GetRawText());
        }

        Assert.True(Newtonsoft.Json.Linq.JToken.DeepEquals(
            Newtonsoft.Json.Linq.JToken.Parse(Encoding.UTF8.GetString(MetadataRewrite.Rewrite(json, "baseline"))),
            Newtonsoft.Json.Linq.JToken.Parse(Encoding.UTF8.GetString(actual))));
    }
}

static void Write(object value) => Console.WriteLine(Stj.JsonSerializer.Serialize(value));
