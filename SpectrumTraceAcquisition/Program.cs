using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using System.Text;
using System.Threading.Tasks;
using Tektronix;
using MathNet.Numerics.Statistics;

namespace SpectrumTraceAcquisition
{
	class Program
	{
		const double hertz2MegaHertz = 1 / 1e6;
		const double hertz2KiloHertz = 1 / 1e3;

		//Class Level Object Variable Declarations
		private static APIWrapper apiWrapper;
		private static Configuration configuration;
		private static SortedDictionary<double, List<double>> powerArraysByFrequencyDictionary;

		//Class Level Measurement Variable Declarations
		private static SpectrumDetectors detectorType;
		private static SpectrumWindows windowType;
		private static SpectrumTraces spectrumTraceNumber;
		private static SpectrumVerticalUnits verticalUnits;
		private static bool enableExtRef;
		private static bool enableVBW;
		private static int traceLength;
		private static int numTraces;
		private static double centerFreq;
		private static double span;
		private static double RBW;
		private static double VBW;
		private static double refLevel;

		static void Main(string[] args)
		{
			//ExamplesMain();
			//Initialize class level object variables
			apiWrapper = new APIWrapper();
			configuration = LoadConfiguration();
			powerArraysByFrequencyDictionary = new SortedDictionary<double, List<double>>();

			//Search for devices.
			int[] devID = null;
			string[] devSN = null;
			string[] devType = null;
			ReturnStatus returnStatus = apiWrapper.DEVICE_Search(ref devID, ref devSN, ref devType);
			if (returnStatus != ReturnStatus.noError) { Console.WriteLine("DEVICE_Search ERROR: " + returnStatus); }

			//Connect to the first device detected.
			returnStatus = apiWrapper.DEVICE_Connect(devID[0]);

			if (returnStatus != ReturnStatus.noError) { Console.WriteLine("DEVICE_Connect ERROR: " + returnStatus); }
			else // print the name of the connected device.
			{
				Console.WriteLine("\nCONNECTED TO: " + devType[0]);
				Console.WriteLine("\nSerial Number:" + devSN[0]);
			}

			int measurementsCount = configuration.Measurements.Count;
			Console.WriteLine("measurementsCount: " + measurementsCount);

			foreach (Measurement measurement in configuration.Measurements)
			{
				AssignMeasurementVariables(measurement);
				
				//CONFIG SPECTRUM
				//TODO: Handle when returnStatus != ReturnStatus.noError
				returnStatus = apiWrapper.ALIGN_RunAlignment();
				if (returnStatus != ReturnStatus.noError) { Console.WriteLine("ALIGN_RunAlignment ERROR: " + returnStatus); }
				returnStatus = apiWrapper.CONFIG_Preset();
				if (returnStatus != ReturnStatus.noError) { Console.WriteLine("CONFIG_Preset ERROR: " + returnStatus); }
				returnStatus = apiWrapper.SPECTRUM_SetEnable(true);
				if (returnStatus != ReturnStatus.noError) { Console.WriteLine("SPECTRUM_SetEnable ERROR: " + returnStatus); }
				returnStatus = apiWrapper.CONFIG_SetCenterFreq(centerFreq);
				if (returnStatus != ReturnStatus.noError) { Console.WriteLine("CONFIG_SetCenterFreq ERROR: " + returnStatus); }
				returnStatus = apiWrapper.CONFIG_SetReferenceLevel(refLevel);
				if (returnStatus != ReturnStatus.noError) { Console.WriteLine("CONFIG_SetReferenceLevel ERROR: " + returnStatus); }
				returnStatus = apiWrapper.CONFIG_SetExternalRefEnable(enableExtRef);
				if (returnStatus != ReturnStatus.noError) { Console.WriteLine("CONFIG_SetExternalRefEnable ERROR: " + returnStatus); }
				returnStatus = apiWrapper.SPECTRUM_SetTraceType(spectrumTraceNumber, true, detectorType);
				if (returnStatus != ReturnStatus.noError) { Console.WriteLine("SPECTRUM_SetTraceType ERROR: " + returnStatus); }

				Spectrum_Limits salimits = new Spectrum_Limits();

				returnStatus = apiWrapper.SPECTRUM_GetLimits(ref salimits);
				if (returnStatus != ReturnStatus.noError) { Console.WriteLine("SPECTRUM_GetLimits ERROR: " + returnStatus); }

				//Check the limits
				if (span > salimits.maxSpan) { span = salimits.maxSpan; }


				// Set and get RSA parameter values.
				Spectrum_Settings setSettings = new Spectrum_Settings();
				//Assign user settings to settings struct.
				setSettings.span = span;
				setSettings.rbw = RBW;
				setSettings.enableVBW = enableVBW;
				setSettings.vbw = VBW;
				setSettings.traceLength = traceLength;
				setSettings.window = windowType;
				setSettings.verticalUnit = verticalUnits;

				Spectrum_Settings getSettings = new Spectrum_Settings();

				returnStatus = apiWrapper.SPECTRUM_SetEnable(true);
				if (returnStatus != ReturnStatus.noError) { Console.WriteLine("SPECTRUM_SetEnable ERROR: " + returnStatus); }
				//returnStatus = apiWrapper.SPECTRUM_SetDefault();
				//if (returnStatus != ReturnStatus.noError) { Console.WriteLine("SPECTRUM_SetDefault ERROR: " + returnStatus); }

				//Register the settings.
				returnStatus = apiWrapper.SPECTRUM_SetSettings(setSettings);
				if (returnStatus != ReturnStatus.noError) { Console.WriteLine("SPECTRUM_SetSettings ERROR: " + returnStatus); }

				//returnStatus = apiWrapper.SPECTRUM_GetSettings(ref getSettings);
				//if (returnStatus != ReturnStatus.noError) { Console.WriteLine("SPECTRUM_GetSettings ERROR: " + returnStatus); }

				Console.WriteLine("\nSet Settings: " + setSettings);


				//Retrieve the settings info.
				returnStatus = apiWrapper.SPECTRUM_GetSettings(ref getSettings);
				if (returnStatus != ReturnStatus.noError) { Console.WriteLine("ERROR: " + returnStatus); }
				Console.WriteLine("\nGet Settings: " + getSettings);


				double actualRBW = getSettings.actualRBW;
				Console.WriteLine("actualRBW: " + actualRBW);
				double actualVBW = getSettings.actualVBW;
				Console.WriteLine("actualVBW: " + actualVBW);
				double numIQsamples = getSettings.actualNumIQSamples;
				Console.WriteLine("numIQsamples: " + numIQsamples);
				int actualTraceLength = getSettings.traceLength;
				Console.WriteLine("actualTraceLength: " + actualTraceLength);
				//Set variables used to calculate freqArray items
				double actualStartFreq = getSettings.actualStartFreq;
				Console.WriteLine("actualStartFreq: " + actualStartFreq);
				double actualStopFreq = getSettings.actualStopFreq;
				Console.WriteLine("actualStopFreq: " + actualStopFreq);
				double actualFreqStepSize = getSettings.actualFreqStepSize;
				Console.WriteLine("actualFreqStepSize: " + actualFreqStepSize);

				//New freqArray length of traceLength
				//double[] freqArray = new double[traceLength];
				//Console.WriteLine("freqArray.Length:" + freqArray.Length);

				//Allocate memory array for spectrum output vector.
				float[] powerArray = null;
				double[] frequencyArray = new double[traceLength];

				//Use for loop to create dictionary items
				for (int arrayIndex = 0; arrayIndex < traceLength; arrayIndex++)
				{
					double frequency = (actualFreqStepSize * arrayIndex) + actualStartFreq;
					frequencyArray[arrayIndex] = frequency;
					List<double> powerListForThisFrequency = null;

					if (powerArraysByFrequencyDictionary.TryGetValue(frequency, out powerListForThisFrequency))
					{
						//TryGetValue() call found pre-existing powerListForThisFrequency in powerArraysByFrequencyDictionary
						//so no need to add a new item at this frequency
					}
					else
					{
						//Pre-existing powerListForThisFrequency in powerArraysByFrequencyDictionary not found so create a new one. Then add a new item
						//to the dictionary with the new item's key being the current frequency value we are at in this iteration of the for loop
						powerArraysByFrequencyDictionary.Add(frequency, new List<double>());
					}
				}

				//Start the trace capture.
				returnStatus = apiWrapper.SPECTRUM_SetEnable(true);
				if (returnStatus != ReturnStatus.noError) { Console.WriteLine("ERROR: " + returnStatus); }

				Console.WriteLine("Trace capture is starting...");
				bool traceReady = false;
				bool isActive = true;
				bool eventOccured = false;
				int waitTimeoutMsec = 1000;//Maximum allowable wait time for each data acquistion.
				int numTimeouts = 3;//Maximum amount of attempts to acquire data if a timeout occurs.
									//Note: the total wait time to acquire data is waitTimeoutMsec x numTimeouts.
				int timeoutCount = 0;//Variable to track the timeouts.
				int traceCount = 0;
				int outTracePoints = 0;
				long eventTimestamp = 0;

				while (isActive)
				{
					returnStatus = apiWrapper.SPECTRUM_AcquireTrace();
					//Wait for the trace to be ready.
					returnStatus = apiWrapper.SPECTRUM_WaitForTraceReady(waitTimeoutMsec, ref traceReady);
					if (returnStatus != ReturnStatus.noError) { Console.WriteLine("ERROR: " + returnStatus); }

					if (traceReady)
					{
						Console.WriteLine("================INPUT MEASUREMENT PARAMETERS=================");
						Console.WriteLine("Start Frequency = {0} MHz", actualStartFreq * hertz2MegaHertz);
						Console.WriteLine("Stop Frequency = {0} MHz", actualStopFreq * hertz2MegaHertz);
						Console.WriteLine("Center Frequency = {0} MHz", centerFreq * hertz2MegaHertz);
						Console.WriteLine("Span = {0} MHz", span * hertz2MegaHertz);
						Console.WriteLine("RBW = {0} kHz", actualRBW * hertz2KiloHertz);
						Console.WriteLine("VBW = {0} MHz", actualVBW);
						Console.WriteLine("Detection = {0}", detectorType);
						Console.WriteLine("Trace Length = {0}", traceLength);
						Console.WriteLine("Number of Traces = {0}", numTraces);
						Console.WriteLine("Reference Level = {0} dB", refLevel);

						////*********************************************Get spectrum trace data.
						returnStatus = apiWrapper.SPECTRUM_GetTrace(spectrumTraceNumber, traceLength, ref powerArray, ref outTracePoints);

						if (returnStatus != ReturnStatus.noError) { Console.WriteLine("ERROR: " + returnStatus); }
						Console.WriteLine("outTracePoints: " + outTracePoints);

						//Get traceInfo struct.
						Spectrum_TraceInfo traceInfo = new Spectrum_TraceInfo();
						returnStatus = apiWrapper.SPECTRUM_GetTraceInfo(ref traceInfo);
						if (returnStatus != ReturnStatus.noError) { Console.WriteLine("ERROR: " + returnStatus); }

						//You can use this information to report any non-zero bits in AcqDataStatus word, for example.
						if (traceInfo.acqDataStatus != 0)
						{
							Console.WriteLine("Trace:" + traceCount + ", AcqDataStatus:" + traceInfo.acqDataStatus, "Timestamp:" + traceInfo.timestamp);
							Console.WriteLine(powerArray.Max());
						}

						//ADC Overload
						EventType overloadDetected = EventType.DEVEVENT_OVERRANGE;
						returnStatus = apiWrapper.DEVICE_GetEventStatus(overloadDetected, ref eventOccured, ref eventTimestamp);
						if (returnStatus != ReturnStatus.noError) { Console.WriteLine("ERROR: " + returnStatus); }

						if (eventOccured)
						{
							Console.WriteLine("ADC OVERLOAD! Adjust Reference Level?:" + eventTimestamp);
							float maxPwr = powerArray.Max();
							Console.WriteLine("Maximum Power Level:" + maxPwr);
						}

						//Trigger Detection
						EventType triggerDetected = EventType.DEVEVENT_TRIGGER;
						returnStatus = apiWrapper.DEVICE_GetEventStatus(triggerDetected, ref eventOccured, ref eventTimestamp);
						if (returnStatus != ReturnStatus.noError) { Console.WriteLine("ERROR: " + returnStatus); }

						if (eventOccured)
						{
							Console.WriteLine("Trigger Detected!");
						}

						//Create a list of power vs frequency values
						for (int arrayIndex = 0; arrayIndex < powerArray.Length; arrayIndex++)
						{
							List<double> powerListForThisFrequency = null;

							double frequency = frequencyArray[arrayIndex];
							float power = powerArray[arrayIndex];

							if (powerArraysByFrequencyDictionary.TryGetValue(frequency, out powerListForThisFrequency))
							{
								//TryGetValue() call found pre-existing powerListForThisFrequency in powerArraysByFrequencyDictionary
								//add the current power value to the powerListForThisFrequency
								if (powerListForThisFrequency.Count < numTraces)
								{
									powerListForThisFrequency.Add(power); 
								}
							}
							else
							{
								//Pre-existing powerListForThisFrequency in powerArraysByFrequencyDictionary not found so create a new one. 
								//Then add the current power value to the new powerListForThisFrequency, then add a new item to the dictionary 
								//with the new item's key being the current frequency value we are at in this iteration of the for loop
								powerListForThisFrequency = new List<double>();
								powerListForThisFrequency.Add(power);

								powerArraysByFrequencyDictionary.Add(frequency, powerListForThisFrequency);
							}
						}

						traceCount++;

						Console.WriteLine("Trace Count:" + traceCount);
					}
					else
					{
						timeoutCount++;
						Console.WriteLine("Timeout Count:" + timeoutCount);
					}

					Console.WriteLine("'numTraces:" + numTraces + "' == " + "'traceCount:" + traceCount + " " + (numTraces == traceCount));
					//Stop acquiring traces when the limit is reached or the wait time is exceeded.
					if (numTraces == traceCount || timeoutCount == numTimeouts)
					{
						isActive = false;
						Console.WriteLine("Is Active?" + isActive);
					}
				}
			}

			string outputFilePath = @"..\..\..\Output\" + DateTime.Now.ToString("yyyy-MM-dd_HH.mm.ss") + "_" + "STARTFREQ" + "-" + "STARTFREQ" + @"_survey.csv";
			string statisticsFilePath = @"..\..\..\Output\" + DateTime.Now.ToString("yyyy-MM-dd_HH.mm.ss") + "_" + "STARTFREQ" + "-" + "STARTFREQ" + @"_M4.csv";

			using (StreamWriter streamWriter = new StreamWriter(outputFilePath, true))
			{
				using (StreamWriter statsStreamWriter = new StreamWriter(statisticsFilePath, true))
				{
					if (numTraces > 1)
					{
						//Write CSV File Headings: Frequency,Minimum,Maximum,Mean,Median
						statsStreamWriter.WriteLine("Frequency,Minimum,Maximum,Mean,Median");
					}
					int dictionaryIndex = 0;
					foreach (KeyValuePair<double, List<double>> keyValuePair in powerArraysByFrequencyDictionary)
					{
						double frequency = keyValuePair.Key;
						List<double> powerList = keyValuePair.Value;
						if (powerList.Count > 0)
						{
							if (numTraces > 1 && powerList.Count > 1)
							{
								double[] powerArrayForStatistics = powerList.ToArray();
								double maximumPower = ArrayStatistics.Maximum(powerArrayForStatistics);
								double minimumPower = ArrayStatistics.Minimum(powerArrayForStatistics);
								double meanPower = (float)ArrayStatistics.Mean(powerArrayForStatistics);
								double medianPower = ArrayStatistics.MedianInplace(powerArrayForStatistics);
								//Write CSV values for this frequency
								statsStreamWriter.WriteLine("{0},{1},{2},{3},{4}", frequency, minimumPower, maximumPower, meanPower, medianPower);
							}
							else //write frequency and power value for single trace when powerList.Count == 1
							{
								streamWriter.WriteLine("{0},{1}", frequency, powerList[0]);// only one power value in List at index 0
							}
						}
						if (dictionaryIndex == 800)
						{
						}
						dictionaryIndex++;
					}
				}
			}

			//Disconnect the device and finish up.
			returnStatus = apiWrapper.SPECTRUM_SetEnable(false);
			returnStatus = apiWrapper.DEVICE_Stop();
			returnStatus = apiWrapper.DEVICE_Disconnect();

			Console.WriteLine("\nSpectrum trace acquisition routine complete.");
			Console.WriteLine("\nPress enter key to exit...");
			Console.ReadKey();
		}

		private static void AssignMeasurementVariables(Measurement measurement)
		{
			detectorType = DetectorTypeShortName2Enum(measurement.DetectorType);
			windowType = WindowTypeShortName2Enum(measurement.WindowType);
			spectrumTraceNumber = SpectrumTraceNumberShortName2Enum(measurement.SpectrumTraceNumber);
			verticalUnits = VerticalUnitsShortName2Enum(measurement.VerticalUnits);
			enableExtRef = measurement.EnableExternalFrequencyRefererence;
			enableVBW = measurement.EnableVBW;
			traceLength = measurement.TraceLength;
			numTraces = measurement.NumberOfTraces;
			centerFreq = measurement.CenterFrequency;
			span = measurement.Span;
			RBW = measurement.RBW;
			VBW = measurement.VBW;
			refLevel = measurement.ReferenceLevel;
		}

		public static Configuration LoadConfiguration()
		{
			Configuration configuration = null;
			XmlSerializer xmlSerializer = new XmlSerializer(typeof(Configuration));
			using (TextReader textReader = new StreamReader(@"..\..\..\Input\Configuration.xml"))
			{
				configuration = (Configuration)xmlSerializer.Deserialize(textReader);
			}
			return configuration;
		}
		public static SpectrumDetectors DetectorTypeShortName2Enum(string detectorTypeShortName)
		{
			// Map the detectorType short string name in Configuraion.xml to a valid enum value.\
			switch (detectorTypeShortName.ToUpper())
			{
				case "AVERAGE":
					return SpectrumDetectors.Average;
				case "NEGPEAK":
					return SpectrumDetectors.NegPeak;
				case "PEAK":
					return SpectrumDetectors.PosPeak;
				case "SAMPLE":
					return SpectrumDetectors.Sample;
				default:
					return SpectrumDetectors.PosPeak;
			}
		}
		public static SpectrumWindows WindowTypeShortName2Enum(string windowTypeShortName)
		{
			switch (windowTypeShortName.ToUpper())
			{
				case "BLACKMANHARRIS":
					return SpectrumWindows.SpectrumWindow_BlackmanHarris;
				case "FLATTOP":
					return SpectrumWindows.SpectrumWindow_FlatTop;
				case "HANN":
					return SpectrumWindows.SpectrumWindow_Hann;
				case "KAISER":
					return SpectrumWindows.SpectrumWindow_Kaiser;
				case "MIL6DB":
					return SpectrumWindows.SpectrumWindow_Mil6dB;
				case "RECTANGLE":
					return SpectrumWindows.SpectrumWindow_Rectangle;
				default:
					return SpectrumWindows.SpectrumWindow_Kaiser;
			}
		}
		public static SpectrumTraces SpectrumTraceNumberShortName2Enum(string spectrumTraceNumberShortName)
		{
			switch (spectrumTraceNumberShortName.ToUpper())
			{
				case "TRACE1":
					return SpectrumTraces.SpectrumTrace1;
				case "TRACE2":
					return SpectrumTraces.SpectrumTrace2;
				case "TRACE3":
					return SpectrumTraces.SpectrumTrace3;
				default:
					return SpectrumTraces.SpectrumTrace1;
			}
		}
		public static SpectrumVerticalUnits VerticalUnitsShortName2Enum(string verticalUnitsShortName)
		{
			switch (verticalUnitsShortName.ToUpper())
			{
				case "AMP":
					return SpectrumVerticalUnits.SpectrumVerticalUnit_Amp;
				case "DBM":
					return SpectrumVerticalUnits.SpectrumVerticalUnit_dBm;
				case "DBMV":
					return SpectrumVerticalUnits.SpectrumVerticalUnit_dBmV;
				case "VOLT":
					return SpectrumVerticalUnits.SpectrumVerticalUnit_Volt;
				case "WATT":
					return SpectrumVerticalUnits.SpectrumVerticalUnit_Watt;
				default:
					return SpectrumVerticalUnits.SpectrumVerticalUnit_dBm;
			}
		}


		//public static EventType EventTypeShortName2Enum(string eventTypeShortName)
		//{ 
		//	switch (eventTypeShortName.ToUpper())
		//          {
		//              case "overloadDetected":
		//                  return EventType.DEVEVENT_OVERRANGE;
		//              case "triggerDetected":
		//                  return EventType.DEVEVENT_TRIGGER;
		//              default:
		//                  return EventType.DEVEVENT_OVERRANGE;
		//	}
		//}


		static void ExamplesMain()
		{
			APIWrapper api = new APIWrapper();
			//Search for devices.
			int[] devID = null;
			string[] devSN = null;
			string[] devType = null;
			ReturnStatus rs = api.DEVICE_Search(ref devID, ref devSN, ref devType);

			//Connect to the first device detected.
			rs = api.DEVICE_Connect(devID[0]);

			//The following is an example on how to use the return status of an API function.
			//For simplicity, it will not be used in the rest of the program.
			//This is a fatal error: the device could not be connected.
			if (rs != ReturnStatus.noError)
			{
				Console.WriteLine("\nERROR: " + rs);
				goto end;
			}
			else // print the name of the connected device.
				Console.WriteLine("\nCONNECTED TO: " + devType[0]);

			// Set the center frequency and reference level.
			rs = api.CONFIG_SetCenterFreq(103.3e6);
			rs = api.CONFIG_SetReferenceLevel(-10);

			//Assign a trace to use. In this example, use trace 1 of 3.
			SpectrumTraces traceID = SpectrumTraces.SpectrumTrace1;
			double span = 40e6;//The span of the trace.
			double rbw = 100; //Resolution bandwidth.
			SpectrumWindows window = SpectrumWindows.SpectrumWindow_Kaiser;//Use the default window (Kaiser).
																		   //SpectrumDetectors detector = SpectrumDetectors.SpectrumDetector_PosPeak;//Use the default detector (positive peak).
			SpectrumVerticalUnits vertunits = SpectrumVerticalUnits.SpectrumVerticalUnit_dBm;//Use the default vertical units (dBm).
			int traceLength = 4097;//Use the default trace length of 801 points.
			int numTraces = 10;//This will be the number of traces to acquire.
			string fn = "TRACE.txt";//This will be the output filename.

			//Get the limits for the spectrum acquisition control settings.
			Spectrum_Limits salimits = new Spectrum_Limits();
			rs = api.SPECTRUM_GetLimits(ref salimits);
			if (span > salimits.maxSpan) span = salimits.maxSpan;//You can use this information to check the limits.

			// Set SA controls to default, and get the control values.
			Spectrum_Settings setSettings = new Spectrum_Settings();
			Spectrum_Settings getSettings = new Spectrum_Settings();
			rs = api.SPECTRUM_SetDefault();
			rs = api.SPECTRUM_GetSettings(ref getSettings);

			//Assign user settings to settings struct.
			setSettings.span = span;
			setSettings.rbw = rbw;
			setSettings.enableVBW = true;
			setSettings.vbw = 100;
			setSettings.traceLength = traceLength;
			setSettings.window = window;
			setSettings.verticalUnit = vertunits;

			//Register the settings.
			rs = api.SPECTRUM_SetSettings(setSettings);

			//Retrieve the settings info.
			rs = api.SPECTRUM_GetSettings(ref getSettings);

			//Open a file for text output.
			System.IO.StreamWriter spectrumFile = new System.IO.StreamWriter(fn);

			//Allocate memory array for spectrum output vector.
			float[] pTraceData = null;

			//Start the trace capture.
			rs = api.SPECTRUM_SetEnable(true);
			Console.WriteLine("\nTrace capture is starting...");
			bool isActive = true;
			int waitTimeoutMsec = 1000;//Maximum allowable wait time for each data acquistion.
			int numTimeouts = 3;//Maximum amount of attempts to acquire data if a timeout occurs.
								//Note: the total wait time to acquire data is waitTimeoutMsec x numTimeouts.
			int timeoutCount = 0;//Variable to track the timeouts.
			int traceCount = 0;
			bool traceReady = false;
			int outTracePoints = 0;

			while (isActive)
			{
				rs = api.SPECTRUM_AcquireTrace();
				//Wait for the trace to be ready.
				rs = api.SPECTRUM_WaitForTraceReady(waitTimeoutMsec, ref traceReady);
				if (traceReady)
				{
					//Get spectrum trace data.
					rs = api.SPECTRUM_GetTrace(traceID, traceLength, ref pTraceData, ref outTracePoints);

					Console.WriteLine("outTracePoints: " + outTracePoints);


					//Get traceInfo struct.
					Spectrum_TraceInfo traceInfo = new Spectrum_TraceInfo();
					rs = api.SPECTRUM_GetTraceInfo(ref traceInfo);
					//You can use this information to report any non-zero bits in AcqDataStatus word, for example.
					if (traceInfo.acqDataStatus != 0)
						Console.WriteLine("\nTrace:" + traceCount + ", AcqDataStatus:" + traceInfo.acqDataStatus);
					Console.WriteLine(pTraceData.Max());

					//Write data to the open file.
					for (int n = 0; n < outTracePoints; n++)
						spectrumFile.Write(pTraceData[n]);
					spectrumFile.Write("\n");

					traceCount++;

				}
				else timeoutCount++;

				//Stop acquiring traces when the limit is reached or the wait time is exceeded.
				if (numTraces == traceCount || timeoutCount == numTimeouts)
					isActive = false;
			}

			//Disconnect the device and finish up.
			rs = api.SPECTRUM_SetEnable(false);
			rs = api.DEVICE_Stop();
			rs = api.DEVICE_Disconnect();

			end:
			Console.WriteLine("\nSpectrum trace acquisition routine complete.");
			Console.WriteLine("\nPress enter key to exit...");
			Console.ReadKey();
		}
	}
}
