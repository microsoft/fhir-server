// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using Bogus;
using Hl7.Fhir.Introspection;
using Hl7.Fhir.Model;
using Hl7.Fhir.Specification;
using Hl7.Fhir.Validation;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Tests.Fakes;

/// <summary>
/// Provides factory methods for creating FHIR resource fakes for testing purposes.
/// </summary>
public static class FhirFakesFactory
{
    private static readonly Faker _fakerFactory = new("en");
    private static readonly ResourceReference _defaultPatientReference = new("Patient/example");

    private static readonly Faker<Patient> _patientFaker = new Faker<Patient>("en")
        .RuleFor(p => p.Id, _ => Guid.NewGuid().ToString("N"))
        .RuleFor(p => p.Gender, f => _fakerFactory.PickRandom<AdministrativeGender>())
        .RuleFor(p => p.BirthDate, f => _fakerFactory.Date.Past(80, DateTime.Today.AddYears(-18)).ToString("yyyy-MM-dd"))
        .FinishWith((f, p) =>
        {
            p.Name.Add(new HumanName
            {
                Use = HumanName.NameUse.Official,
                Family = _fakerFactory.Name.LastName(),
                Given = [_fakerFactory.Name.FirstName()],
            });
            p.Identifier.Add(new Identifier("http://hospital.local/mrn", _fakerFactory.Random.Number(1_000_000, 9_999_999).ToString()));
        });

    private static readonly Faker<Encounter> _encounterFaker = new Faker<Encounter>("en")
        .RuleFor(e => e.Id, _ => Guid.NewGuid().ToString("N"))
        .RuleFor(e => e.Status, _ => Encounter.EncounterStatus.Finished)
        .RuleFor(e => e.Class, _ => new Coding("http://terminology.hl7.org/CodeSystem/v3-ActCode", "AMB", "ambulatory"))
        .RuleFor(e => e.Participant, _ => [new Encounter.ParticipantComponent { Individual = _defaultPatientReference }])
        .FinishWith((f, e) =>
        {
            DateTime start = _fakerFactory.Date.Past(1);
            e.Period = new Period
            {
                Start = start.ToString("o"),
                End = start.AddHours(_fakerFactory.Random.Double(0.5, 3)).ToString("o"),
            };
        });

    private static readonly Faker<Condition> _conditionFaker = new Faker<Condition>("en")
        .RuleFor(c => c.Id, _ => Guid.NewGuid().ToString("N"))
        .RuleFor(c => c.ClinicalStatus, _ => new CodeableConcept("http://terminology.hl7.org/CodeSystem/condition-clinical", "active"))
        .RuleFor(c => c.Code, _ => new CodeableConcept("http://snomed.info/sct", "44054006", "Diabetes mellitus"))
        .RuleFor(c => c.Subject, _ => _defaultPatientReference);

    private static readonly Faker<MedicationRequest> _medReqFaker = new Faker<MedicationRequest>("en")
        .RuleFor(m => m.Id, _ => Guid.NewGuid().ToString("N"))
        .RuleFor(m => m.Status, _ => MedicationRequest.MedicationrequestStatus.Active)
        .RuleFor(m => m.Intent, _ => _fakerFactory.PickRandom<MedicationRequest.MedicationRequestIntent>())
        .RuleFor(m => m.Medication, _ => new CodeableConcept("http://www.nlm.nih.gov/research/umls/rxnorm", "243670", "Metformin 500 MG"))
        .RuleFor(c => c.Subject, _ => _defaultPatientReference);

    private static readonly Faker<Immunization> _immunizationFaker = new Faker<Immunization>("en")
        .RuleFor(i => i.Id, _ => Guid.NewGuid().ToString("N"))
        .RuleFor(i => i.Status, _ => Immunization.ImmunizationStatusCodes.Completed)
        .RuleFor(i => i.VaccineCode, _ => new CodeableConcept("http://hl7.org/fhir/sid/cvx", "207", "COVID‑19, mRNA, LNP‑Encapsulated"))
        .RuleFor(i => i.Occurrence, f => new FhirDateTime(_fakerFactory.Date.Past(2)))
        .RuleFor(c => c.Patient, _ => _defaultPatientReference);

    private static readonly Faker<DiagnosticReport> _dxReportFaker = new Faker<DiagnosticReport>("en")
        .RuleFor(d => d.Id, _ => Guid.NewGuid().ToString("N"))
        .RuleFor(d => d.Status, _ => DiagnosticReport.DiagnosticReportStatus.Final)
        .RuleFor(d => d.Code, _ => new CodeableConcept("http://loinc.org", "58410-2", "Complete blood count panel"))
        .RuleFor(c => c.Subject, _ => _defaultPatientReference);

    private static readonly Faker<Procedure> _procedureFaker = new Faker<Procedure>("en")
        .RuleFor(p => p.Id, _ => Guid.NewGuid().ToString("N"))
        .RuleFor(p => p.Status, _ => EventStatus.Completed)
        .RuleFor(p => p.Code, _ => new CodeableConcept("http://snomed.info/sct", "80146002", "Appendectomy"))
        .RuleFor(c => c.Subject, _ => _defaultPatientReference);

    private static readonly Faker<Observation> _observationFaker = new Faker<Observation>("en")
        .RuleFor(o => o.Id, _ => Guid.NewGuid().ToString("N"))
        .RuleFor(o => o.Status, _ => ObservationStatus.Final)
        .RuleFor(o => o.Code, _ => new CodeableConcept("http://loinc.org", "29463-7", "Body weight"))
        .RuleFor(o => o.Effective, f => new FhirDateTime(_fakerFactory.Date.Recent()))
        .RuleFor(o => o.Value, f => new Quantity(_fakerFactory.Random.Decimal(40, 120), "kg"))
        .RuleFor(c => c.Subject, _ => _defaultPatientReference);

    private static readonly Faker<Attachment> _attachmentFaker = new Faker<Attachment>("en")
        .RuleFor(x => x.ContentType, _ => _fakerFactory.PickRandom("text/plain"))
        .RuleFor(x => x.Data, _ => Encoding.UTF8.GetBytes(_fakerFactory.Lorem.Sentence()));

    // ──────────────────────────────────────────────────────────────────────────
    //  Registry: maps resource types to factories that *link* them to patient
    // ──────────────────────────────────────────────────────────────────────────

    private static readonly Dictionary<Type, Func<Base>> _factories = new()
    {
        { typeof(Patient), () => _patientFaker.Generate() },
        { typeof(Condition), () => _conditionFaker.Generate() },
        { typeof(MedicationRequest), () => _medReqFaker.Generate() },
        { typeof(Immunization), () => _immunizationFaker.Generate() },
        { typeof(DiagnosticReport), () => _dxReportFaker.Generate() },
        { typeof(Procedure), () => _procedureFaker.Generate() },
        { typeof(Observation), () => _observationFaker.Generate() },
        { typeof(Encounter), () => _encounterFaker.Generate() },
        { typeof(Attachment), () => _attachmentFaker.Generate() },
    };

    /// <summary>
    /// Helper to clone faker with optional deterministic seed
    /// </summary>
    private static Faker<T> UseSeed<T>(this Faker<T> faker, int? seed)
        where T : class
    {
        return seed.HasValue ? faker.Clone().UseSeed(seed.Value) : faker;
    }

    /// <summary>
    /// Creates a new fake <see cref="Patient"/> resource.
    /// </summary>
    /// <param name="seed">An optional seed for deterministic generation.</param>
    /// <returns>A new <see cref="Patient"/> instance.</returns>
    public static Patient CreatePatient(int? seed = null)
    {
        return _patientFaker.Clone().UseSeed(seed).Generate();
    }

    /// <summary>
    /// Creates a new fake <see cref="Encounter"/> resource, linked to the specified patient.
    /// </summary>
    /// <param name="p">The <see cref="Patient"/> to link the encounter to.</param>
    /// <param name="seed">An optional seed for deterministic generation.</param>
    /// <returns>A new <see cref="Encounter"/> instance.</returns>
    public static Encounter CreateEncounter(Patient p, int? seed = null)
    {
        return _encounterFaker.Clone().UseSeed(seed)
            .RuleFor(e => e.Subject, _ => new ResourceReference($"Patient/{p.Id}"))
            .Generate();
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Public API – create linked resources of any Patient‑compartment type
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new fake FHIR resource of the specified type.
    /// If the resource type is known (e.g., Patient, Observation), a pre-configured faker is used.
    /// Otherwise, a generic reflection-based mechanism attempts to create and populate the resource.
    /// The created resource will be linked to a default patient reference if applicable.
    /// </summary>
    /// <typeparam name="T">The type of FHIR resource to create. Must be a subclass of <see cref="Resource"/>.</typeparam>
    /// <returns>A new instance of the specified FHIR resource type.</returns>
    public static T Create<T>()
        where T : Resource
    {
        return (T)Create(typeof(T));
    }

    /// <summary>
    /// Creates a new fake FHIR resource of the specified type.
    /// If the resource type is known (e.g., Patient, Observation), a pre-configured faker is used.
    /// Otherwise, a generic reflection-based mechanism attempts to create and populate the resource.
    /// The created resource will be linked to a default patient reference if applicable.
    /// </summary>
    /// <param name="type">The type of FHIR resource to create. Must be assignable to <see cref="Resource"/>.</param>
    /// <returns>A new instance of the specified FHIR resource type.</returns>
    /// <exception cref="ArgumentException">Thrown if the provided type is not a FHIR resource.</exception>
    public static Resource Create(Type type)
    {
        if (!typeof(Resource).IsAssignableFrom(type))
        {
            throw new ArgumentException($"Type {type.Name} is not a FHIR Resource.", nameof(type));
        }

        Resource res;

        if (_factories.TryGetValue(type, out var factory))
        {
            res = (Resource)factory();
        }
        else
        {
            res = CreateGenericResource(type);
        }

        // Link to example patient if possible using the extension method.
        res.LinkToPatient(_defaultPatientReference);

        return res;
    }

    /// <summary>
    /// Creates a <see cref="Bundle"/> containing a patient and a set of related resources
    /// forming a patient compartment.
    /// </summary>
    /// <param name="observations">Number of Observation resources to create.</param>
    /// <param name="conditions">Number of Condition resources to create.</param>
    /// <param name="encounter">Number of Encounter resources to create.</param>
    /// <param name="meds">Number of MedicationRequest resources to create.</param>
    /// <param name="immunizations">Number of Immunization resources to create.</param>
    /// <param name="procedures">Number of Procedure resources to create.</param>
    /// <param name="reports">Number of DiagnosticReport resources to create.</param>
    /// <param name="seed">An optional seed for deterministic generation of the patient.</param>
    /// <returns>A batch <see cref="Bundle"/> with the patient and linked resources.</returns>
    public static Bundle CreatePatientCompartmentBundle(
        int observations = 2,
        int conditions = 1,
        int encounter = 1,
        int meds = 1,
        int immunizations = 1,
        int procedures = 0,
        int reports = 0,
        int? seed = null)
    {
        Patient patient = CreatePatient(seed);
        var bundle = new Bundle { Type = Bundle.BundleType.Batch };

        void Add(Resource r)
        {
            bundle.Entry.Add(new Bundle.EntryComponent
            {
                Resource = r,
                Request = new Bundle.RequestComponent
                {
                    Method = Bundle.HTTPVerb.PUT,
                    Url = $"{r.TypeName}/{r.Id}",
                },
            });
        }

        Add(patient);

        for (int i = 0; i < observations; i++)
        {
            Add(Create<Observation>().LinkToPatient(patient));
        }

        for (int i = 0; i < conditions; i++)
        {
            Add(Create<Condition>().LinkToPatient(patient));
        }

        for (int i = 0; i < encounter; i++)
        {
            Add(Create<Encounter>().LinkToPatient(patient));
        }

        for (int i = 0; i < meds; i++)
        {
            Add(Create<MedicationRequest>().LinkToPatient(patient));
        }

        for (int i = 0; i < immunizations; i++)
        {
            Add(Create<Immunization>().LinkToPatient(patient));
        }

        for (int i = 0; i < procedures; i++)
        {
            Add(Create<Procedure>().LinkToPatient(patient));
        }

        for (int i = 0; i < reports; i++)
        {
            Add(Create<DiagnosticReport>().LinkToPatient(patient));
        }

        return bundle;
    }

    /// <summary>
    /// Generic reflection‑based resource generator that *respects* Required
    /// </summary>
    private static Resource CreateGenericResource(Type resourceType)
    {
        if (resourceType.IsAbstract || resourceType.GetConstructor(Type.EmptyTypes) == null)
        {
            throw new NotSupportedException($"Cannot instantiate {resourceType.Name} generically.");
        }

        var res = (Resource)Activator.CreateInstance(resourceType)!;
        res.Id = Guid.NewGuid().ToString("N");

        PopulateEasyProperties(res);
        EnsureRequiredPropertiesInternal(res);
        return res;
    }

    // First‑pass: primitives & lists
    private static void PopulateEasyProperties(object obj)
    {
        foreach (PropertyInfo prop in obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!ShouldBePopulated(prop))
            {
                continue;
            }

            Type t = prop.PropertyType;

            if (prop.Name is "Contained" or "Extension" or "ModifierExtension")
            {
                continue;
            }

            if (typeof(IList).IsAssignableFrom(t) && t.IsGenericType)
            {
                prop.SetValue(obj, Activator.CreateInstance(t));
                continue;
            }

            var prim = FakePrimitive(t);
            if (prim != null)
            {
                try
                {
                    prop.SetValue(obj, prim);
                }
                catch (Exception e)
                {
                    Trace.WriteLine("Unable to set property " + prop.Name + " on " + obj.GetType().Name + ": " + e.Message);
                }
            }
        }
    }

    // Second‑pass: ensure [Required]/[Cardinality(min>0)] are satisfied
    // Made internal so it can be called by the extension method
    internal static void EnsureRequiredPropertiesInternal(object obj)
    {
        foreach (PropertyInfo prop in obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!ShouldBePopulated(prop))
            {
                continue;
            }

            object current = prop.GetValue(obj);
            bool needsValue = current == null || (current is IList list && list.Count == 0);
            if (!needsValue)
            {
                continue;
            }

            var allowedTypes = prop.GetCustomAttribute<AllowedTypesAttribute>();
            object generated = GenerateValueForType(prop.PropertyType, allowedTypes?.Types);
            if (generated != null)
            {
                try
                {
                    prop.SetValue(obj, generated);
                }
                catch (Exception e)
                {
                    Trace.WriteLine("Unable to set property " + prop.Name + " on " + obj.GetType().Name + ": " + e.Message);
                }
            }
        }
    }

    private static bool ShouldBePopulated(PropertyInfo prop)
    {
        if (!prop.CanWrite
            || prop.SetMethod?.IsStatic == true
            || prop.GetIndexParameters().Length > 0
            || prop.GetCustomAttribute<DataMemberAttribute>() == null)
        {
            return false;
        }

        var required = prop.GetCustomAttributes().Any(a => a.GetType().Name == "RequiredAttribute");
        var card = prop.GetCustomAttribute<CardinalityAttribute>();
        var isMapped = prop.GetCustomAttribute<NotMappedAttribute>();
        var fhirElement = prop.GetCustomAttribute<FhirElementAttribute>();
        if (card?.Min > 0)
        {
            required = true;
        }

        var fhirVersion = Enum.Parse<FhirRelease>(ModelInfoProvider.Version.ToString());
        if (isMapped != null && isMapped?.Since <= fhirVersion)
        {
            // NotMapped properties are not to be set by the factory
            required = false;
        }

        if (fhirElement != null && fhirElement?.Since > fhirVersion)
        {
            // Mapped in a future version, skip
            required = false;
        }

        return required;
    }

    private static object FakePrimitive(Type t)
    {
        // 1) CLR primitives ---------------------------------------------------
        if (t == typeof(string))
        {
            return _fakerFactory.Lorem.Sentence();
        }

        if (t == typeof(bool))
        {
            return _fakerFactory.Random.Bool();
        }

        if (t == typeof(int))
        {
            return _fakerFactory.Random.Int(-10_000, 10_000);
        }

        if (t == typeof(long))
        {
            return _fakerFactory.Random.Long(-1_000_000L, 1_000_000L);
        }

        if (t == typeof(decimal))
        {
            return Convert.ToDecimal(_fakerFactory.Random.Double(-1_000, 1_000));
        }

        if (t == typeof(double))
        {
            return _fakerFactory.Random.Double(-1_000, 1_000);
        }

        if (t == typeof(float))
        {
            return (float)_fakerFactory.Random.Double(-1_000, 1_000);
        }

        if (t == typeof(DateTime))
        {
            return _fakerFactory.Date.Recent();
        }

        if (t == typeof(DateTimeOffset))
        {
            return _fakerFactory.Date.RecentOffset();
        }

        if (t.IsEnum)
        {
            return _fakerFactory.PickRandom(Enum.GetValues(t));
        }

        // 2) FHIR primitives --------------------------------------------------
        // Numeric-ish
        if (t == typeof(Integer))
        {
            return new Integer(_fakerFactory.Random.Int(-1_000, 1_000));
        }

        if (t == typeof(Integer64))
        {
            return new Integer64(_fakerFactory.Random.Long(-1_000_000, 1_000_000));
        }

        if (t == typeof(UnsignedInt))
        {
            return new UnsignedInt(_fakerFactory.Random.Int(0, 1_000_000));
        }

        if (t == typeof(PositiveInt))
        {
            return new PositiveInt(_fakerFactory.Random.Int(1, 1_000_000));
        }

        if (t == typeof(FhirDecimal))
        {
            return new FhirDecimal(Convert.ToDecimal(_fakerFactory.Random.Double(0, 1_000)));
        }

        if (t == typeof(Money))
        {
            var m = new Money
            {
                Value = (decimal)_fakerFactory.Random.Double(0, 1_000),
                Currency = _fakerFactory.PickRandom<Money.Currencies>(),
            };
            return m;
        }

        // Boolean & binary
        if (t == typeof(FhirBoolean))
        {
            return new FhirBoolean(_fakerFactory.Random.Bool());
        }

        if (t == typeof(Base64Binary))
        {
            return new Base64Binary(_fakerFactory.Random.Bytes(_fakerFactory.Random.Int(10, 9999)));
        }

        // Strings & codes
        if (t == typeof(FhirString))
        {
            return new FhirString(_fakerFactory.Lorem.Word());
        }

        if (t == typeof(Code))
        {
            return new Code(_fakerFactory.Random.Word());
        }

        if (t == typeof(Id))
        {
            return new Id(Guid.NewGuid().ToString("N").Substring(0, 16));
        }

        if (t == typeof(Oid))
        {
            return new Oid("urn:oid:1.2." + _fakerFactory.Random.Int(100, 9999));
        }

        if (t == typeof(Uuid))
        {
            return new Uuid("urn:uuid:" + Guid.NewGuid());
        }

        if (t == typeof(Uri))
        {
            return new Uri("http://example.org/" + _fakerFactory.Random.AlphaNumeric(8));
        }

        if (t == typeof(Canonical))
        {
            return new Canonical("http://example.org/" + _fakerFactory.Random.AlphaNumeric(6));
        }

        if (t == typeof(FhirUri))
        {
            return new FhirUri("http://example.org/" + _fakerFactory.Random.AlphaNumeric(6));
        }

        if (t == typeof(FhirUrl))
        {
            return new FhirUrl("http://example.org/" + _fakerFactory.Random.AlphaNumeric(6));
        }

        if (t == typeof(Markdown))
        {
            return new Markdown("**" + _fakerFactory.Lorem.Word() + "**");
        }

        // Date / time
        if (t == typeof(Date))
        {
            return new Date(_fakerFactory.Date.Past(10).Year);
        }

        if (t == typeof(Instant))
        {
            return new Instant(_fakerFactory.Date.RecentOffset());
        }

        if (t == typeof(FhirDateTime))
        {
            return new FhirDateTime(_fakerFactory.Date.RecentOffset(365));
        }

        if (t == typeof(Time))
        {
            return new Time(_fakerFactory.Date.Recent().ToString("HH:mm:ss"));
        }

        // other
        // 1a) Code<T> generic --------------------------------------------------
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Code<>))
        {
            var enumType = t.GetGenericArguments()[0];
            var enumVal = enumType.IsEnum ? Enum.GetValues(enumType).GetValue(0) : null;
            var instance = Activator.CreateInstance(t)!;
            var valProp = t.GetProperty("Value");
            if (valProp != null && enumVal != null)
            {
                valProp.SetValue(instance, enumVal);
            }

            return instance;
        }

        // If it's a PrimitiveType subclass we didn't explicitly code, try generic reflection
        if (typeof(PrimitiveType).IsAssignableFrom(t))
        {
            throw new NotSupportedException($"Primitive type {t.Name} is not supported by FhirFakesFactory.");
        }

        return null;
    }

    // Generates a *minimal* plausible value for the given type
    private static object GenerateValueForType(Type t, Type[] allowedTypesTypes)
    {
        if (t == typeof(DataType) && allowedTypesTypes != null)
        {
            // Generate data for a choice property
            return GenerateValueForType(_fakerFactory.Random.ArrayElement(allowedTypesTypes), null);
        }

        if (typeof(IList).IsAssignableFrom(t) && t.IsGenericType)
        {
            var list = (IList)Activator.CreateInstance(t)!;
            Type elemType = t.GetGenericArguments()[0];
            object item = GenerateValueForType(elemType, allowedTypesTypes) ?? FakePrimitive(elemType) ?? (elemType.IsClass ? Activator.CreateInstance(elemType) : null);
            if (item != null)
            {
                EnsureRequiredPropertiesInternal(item);
                list.Add(item);
            }

            return list;
        }

        // Check our factories
        if (_factories.TryGetValue(t, out var factory))
        {
            return factory();
        }

        if (t == typeof(ResourceReference))
        {
            return new ResourceReference("http://example.org/fhir/Unknown/" + Guid.NewGuid().ToString("N"));
        }

        if (t == typeof(Identifier))
        {
            return new Identifier("http://example.org/fhir/sid/tmp", Guid.NewGuid().ToString("N"));
        }

        if (t == typeof(CodeableConcept))
        {
            return new CodeableConcept("http://example.org", "example", "Example");
        }

        if (t == typeof(Quantity))
        {
            return new Quantity(_fakerFactory.Random.Decimal(1, 100), "1");
        }

        if (t == typeof(Period))
        {
            return new Period(
                FhirDateTime.Now(),
                new FhirDateTime(DateTime.UtcNow.AddHours(1)));
        }

        if (t == typeof(Coding))
        {
            return new Coding("http://terminology.hl7.org/CodeSystem/", "110100", _fakerFactory.Lorem.Word());
        }

        var prim = FakePrimitive(t);
        if (prim != null)
        {
            return prim;
        }

        if (t.IsClass && t.GetConstructor(Type.EmptyTypes) != null)
        {
            // Shallow instance and attempt to fill its own required props recursively
            object inst = Activator.CreateInstance(t)!;
            EnsureRequiredPropertiesInternal(inst);
            return inst;
        }

        return null;
    }
}
