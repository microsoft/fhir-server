using System;

namespace ResourceProcessorNamespace.ExplanationOfBenefit
{
    // Machine generated, modified to have all properties nullable, modified to handle name collisions with CS keywords.
    public class Rootobject
    {
        public string resourceType { get; set; }
        public string id { get; set; }
        public Contained[] contained { get; set; }
        public Identifier[] identifier { get; set; }
        public string status { get; set; }
        public Type type { get; set; }
        public string use { get; set; }
        public Patient patient { get; set; }
        public Billableperiod billablePeriod { get; set; }
        public DateTime? created { get; set; }
        public Insurer insurer { get; set; }
        public Provider provider { get; set; }
        public Referral referral { get; set; }
        public Claim claim { get; set; }
        public string outcome { get; set; }
        public Careteam[] careTeam { get; set; }
        public Insurance[] insurance { get; set; }
        public Item[] item { get; set; }
        public Total[] total { get; set; }
        public Payment payment { get; set; }
    }

    public class Type
    {
        public Coding[] coding { get; set; }
    }

    public class Coding
    {
        public string system { get; set; }
        public string code { get; set; }
    }

    public class Patient
    {
        public string reference { get; set; }
    }

    public class Billableperiod
    {
        public DateTime? start { get; set; }
        public DateTime? end { get; set; }
    }

    public class Insurer
    {
        public string display { get; set; }
    }

    public class Provider
    {
        public string reference { get; set; }
    }

    public class Referral
    {
        public string reference { get; set; }
    }

    public class Claim
    {
        public string reference { get; set; }
    }

    public class Payment
    {
        public Amount amount { get; set; }
    }

    public class Amount
    {
        public float? value { get; set; }
        public string currency { get; set; }
    }

    public class Contained
    {
        public string resourceType { get; set; }
        public string id { get; set; }
        public string status { get; set; }
        public string intent { get; set; }
        public Subject subject { get; set; }
        public Requester requester { get; set; }
        public Performer[] performer { get; set; }
        public Type1 type { get; set; }
        public Beneficiary beneficiary { get; set; }
        public Payor[] payor { get; set; }
    }

    public class Subject
    {
        public string reference { get; set; }
    }

    public class Requester
    {
        public string reference { get; set; }
    }

    public class Type1
    {
        public string text { get; set; }
    }

    public class Beneficiary
    {
        public string reference { get; set; }
    }

    public class Performer
    {
        public string reference { get; set; }
    }

    public class Payor
    {
        public string display { get; set; }
    }

    public class Identifier
    {
        public string system { get; set; }
        public string value { get; set; }
    }

    public class Careteam
    {
        public int? sequence { get; set; }
        public Provider1 provider { get; set; }
        public Role role { get; set; }
    }

    public class Provider1
    {
        public string reference { get; set; }
    }

    public class Role
    {
        public Coding1[] coding { get; set; }
    }

    public class Coding1
    {
        public string system { get; set; }
        public string code { get; set; }
        public string display { get; set; }
    }

    public class Insurance
    {
        public bool? focal { get; set; }
        public Coverage coverage { get; set; }
    }

    public class Coverage
    {
        public string reference { get; set; }
        public string display { get; set; }
    }

    public class Item
    {
        public int? sequence { get; set; }
        public Category category { get; set; }
        public Productorservice productOrService { get; set; }
        public Servicedperiod servicedPeriod { get; set; }
        public Locationcodeableconcept locationCodeableConcept { get; set; }
        public Encounter[] encounter { get; set; }
    }

    public class Category
    {
        public Coding2[] coding { get; set; }
    }

    public class Coding2
    {
        public string system { get; set; }
        public string code { get; set; }
        public string display { get; set; }
    }

    public class Productorservice
    {
        public Coding3[] coding { get; set; }
        public string text { get; set; }
    }

    public class Coding3
    {
        public string system { get; set; }
        public string code { get; set; }
        public string display { get; set; }
    }

    public class Servicedperiod
    {
        public DateTime? start { get; set; }
        public DateTime? end { get; set; }
    }

    public class Locationcodeableconcept
    {
        public Coding4[] coding { get; set; }
    }

    public class Coding4
    {
        public string system { get; set; }
        public string code { get; set; }
        public string display { get; set; }
    }

    public class Encounter
    {
        public string reference { get; set; }
    }

    public class Total
    {
        public Category1 category { get; set; }
        public Amount1 amount { get; set; }
    }

    public class Category1
    {
        public Coding5[] coding { get; set; }
        public string text { get; set; }
    }

    public class Coding5
    {
        public string system { get; set; }
        public string code { get; set; }
        public string display { get; set; }
    }

    public class Amount1
    {
        public float? value { get; set; }
        public string currency { get; set; }
    }
}
