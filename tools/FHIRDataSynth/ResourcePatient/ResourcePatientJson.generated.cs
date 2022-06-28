using System;

namespace ResourceProcessorNamespace.Patient
{
    // Machine generated, modified to have all properties nullable, modified to handle name collisions with CS keywords.
    public class Rootobject
    {
        public string resourceType { get; set; }
        public string id { get; set; }
        public Text text { get; set; }
        public Extension[] extension { get; set; }
        public Identifier[] identifier { get; set; }
        public Name[] name { get; set; }
        public Telecom[] telecom { get; set; }
        public string gender { get; set; }
        public string birthDate { get; set; }
        public DateTime? deceasedDateTime { get; set; }
        public Address[] address { get; set; }
        public Maritalstatus maritalStatus { get; set; }
        public bool? multipleBirthBoolean { get; set; }
        public Communication[] communication { get; set; }
    }

    public class Text
    {
        public string status { get; set; }
        public string div { get; set; }
    }

    public class Maritalstatus
    {
        public Coding[] coding { get; set; }
        public string text { get; set; }
    }

    public class Coding
    {
        public string system { get; set; }
        public string code { get; set; }
        public string display { get; set; }
    }

    public class Extension
    {
        public string url { get; set; }
        public string valueString { get; set; }
        public Valueaddress valueAddress { get; set; }
        public float? valueDecimal { get; set; }
    }

    public class Valueaddress
    {
        public string city { get; set; }
        public string state { get; set; }
        public string country { get; set; }
    }

    public class Identifier
    {
        public string system { get; set; }
        public string value { get; set; }
        public Type type { get; set; }
    }

    public class Type
    {
        public Coding1[] coding { get; set; }
        public string text { get; set; }
    }

    public class Coding1
    {
        public string system { get; set; }
        public string code { get; set; }
        public string display { get; set; }
    }

    public class Name
    {
        public string use { get; set; }
        public string family { get; set; }
        public string[] given { get; set; }
        public string[] prefix { get; set; }
    }

    public class Telecom
    {
        public string system { get; set; }
        public string value { get; set; }
        public string use { get; set; }
    }

    public class Address
    {
        public Extension1[] extension { get; set; }
        public string[] line { get; set; }
        public string city { get; set; }
        public string state { get; set; }
        public string postalCode { get; set; }
        public string country { get; set; }
    }

    public class Extension1
    {
        public string url { get; set; }
        public Extension2[] extension { get; set; }
    }

    public class Extension2
    {
        public string url { get; set; }
        public float? valueDecimal { get; set; }
    }

    public class Communication
    {
        public Language language { get; set; }
    }

    public class Language
    {
        public Coding2[] coding { get; set; }
        public string text { get; set; }
    }

    public class Coding2
    {
        public string system { get; set; }
        public string code { get; set; }
        public string display { get; set; }
    }
}
