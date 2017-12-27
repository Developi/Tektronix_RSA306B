using System.Collections.Generic;

public class Configuration
{
    public List<Measurement> Measurements { get; set; }
}
public class Measurement
{
    public bool EnableExternalFrequencyRefererence { get; set; }
    public bool EnableVBW { get; set; }
    public string DetectorType { get; set; }
    public string WindowType { get; set; }
    public string VerticalUnits { get; set; }
    public string SpectrumTraceNumber { get; set; }
    public int NumberOfTraces { get; set; }
    public int TraceLength { get; set; }
    public double CenterFrequency { get; set; }
    public double Span { get; set; }
    public double RBW { get; set; }
    public double VBW { get; set; }
    public double ReferenceLevel { get; set; }
}

