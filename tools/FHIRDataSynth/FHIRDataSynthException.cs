using System;

namespace FHIRDataSynth
{
    public class FHIRDataSynthException : Exception
    {
        public FHIRDataSynthException(string message)
            : base(message)
        {
        }

        public FHIRDataSynthException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
