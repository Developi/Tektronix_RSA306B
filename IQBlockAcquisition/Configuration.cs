using System.Collections.Generic;

public class Configuration
{
	public List<Measurement> Measurements { get; set; }
}
public class Measurement
{
	public bool EnableExternalFrequencyRefererence { get; set; }
	public string DetectorType { get; set; }
	public string WindowType { get; set; }
	public string VerticalUnits { get; set; }
	public string SpectrumTraceNumber { get; set; }
	public int NumberOfAcquisitions { get; set; }
	public int RecordLength { get; set; }
	public double CenterFrequency { get; set; }
	public double IQBandwidth { get; set; }
	public double ReferenceLevel { get; set; }
	public double TriggerPosition { get; set; }
} 

