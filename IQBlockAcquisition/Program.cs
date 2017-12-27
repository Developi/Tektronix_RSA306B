using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using System.Text;
using System.Threading.Tasks;
using Tektronix;
//using MathNet.Numerics.Statistics;

namespace IQBlockAcquisition
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
		private static bool enableExtRef;
		private static int recordLength;
		private static int numAcqs;
		private static double centerFreq;
		private static double iqBandwidth;
		private static double refLevel;
		private static double triggerPosition;

		static void Main(string[] args)
		{
			//Initialize class level object variables
			apiWrapper = new APIWrapper();
			configuration = LoadConfiguration();
			powerArraysByFrequencyDictionary = new SortedDictionary<double, List<double>>();

			//Search for devices.
			int[] devID = null;
			string[] devSN = null;
			string[] devType = null;
			ReturnStatus returnStatus = apiWrapper.DEVICE_Search(ref devID, ref devSN, ref devType);
			if (returnStatus != ReturnStatus.noError) { Console.WriteLine("ERROR: " + returnStatus); }

			//Connect to the first device detected.
			returnStatus = apiWrapper.DEVICE_Connect(devID[0]);
			if (returnStatus != ReturnStatus.noError) { Console.WriteLine("ERROR: " + returnStatus); }
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

				//CONFIG 
				//TODO: Handle when returnStatus != ReturnStatus.noError
				returnStatus = apiWrapper.ALIGN_RunAlignment();
				if (returnStatus != ReturnStatus.noError) { Console.WriteLine("ERROR: " + returnStatus); }
				returnStatus = apiWrapper.CONFIG_Preset();
				if (returnStatus != ReturnStatus.noError) { Console.WriteLine("ERROR: " + returnStatus); }
				returnStatus = apiWrapper.IQBLK_SetIQRecordLength(recordLength);
				if (returnStatus != ReturnStatus.noError) { Console.WriteLine("ERROR: " + returnStatus); }
				returnStatus = apiWrapper.CONFIG_SetCenterFreq(centerFreq);
				if (returnStatus != ReturnStatus.noError) { Console.WriteLine("ERROR: " + returnStatus); }
				returnStatus = apiWrapper.CONFIG_SetExternalRefEnable(enableExtRef);
				if (returnStatus != ReturnStatus.noError) { Console.WriteLine("ERROR: " + returnStatus); }
				returnStatus = apiWrapper.CONFIG_SetReferenceLevel(refLevel);
				if (returnStatus != ReturnStatus.noError) { Console.WriteLine("ERROR: " + returnStatus); }
				//returnStatus = apiWrapper.TRIG_SetTriggerPositionPercent();
				//if (returnStatus != ReturnStatus.noError) { Console.WriteLine("ERROR: " + returnStatus); }
				returnStatus = apiWrapper.IQBLK_SetIQBandwidth(iqBandwidth);
				if (returnStatus != ReturnStatus.noError) { Console.WriteLine("ERROR: " + returnStatus); }
				returnStatus = apiWrapper.TRIG_SetTriggerPositionPercent(triggerPosition);
				if (returnStatus != ReturnStatus.noError) { Console.WriteLine("ERROR: " + returnStatus); }

				//Line87 in SpectrumTraceAcquisition
				////Check the limits
				//if (span > salimits.maxSpan) { span = salimits.maxSpan; }

				//
				//line 97 in SpectrumTraceAcquisition (new Settings)

				//Check the acquisition parameter limits.
				double maxBW = 0, minBW = 0;
				int maxSamples = 0;
				returnStatus = apiWrapper.IQBLK_GetMinIQBandwidth(ref minBW);
				if (returnStatus != ReturnStatus.noError) { Console.WriteLine("ERROR: " + returnStatus); }
				returnStatus = apiWrapper.IQBLK_GetMaxIQBandwidth(ref maxBW);
				if (returnStatus != ReturnStatus.noError) { Console.WriteLine("ERROR: " + returnStatus); }
				apiWrapper.IQBLK_GetMaxIQRecordLength(ref maxSamples);
				if (returnStatus != ReturnStatus.noError) { Console.WriteLine("ERROR: " + returnStatus); }

				//Get the IQ bandwidth and sample rate and print the values.
				double sampleRate = 0;
				returnStatus = apiWrapper.IQBLK_GetIQBandwidth(ref iqBandwidth);
				returnStatus = apiWrapper.IQBLK_GetIQSampleRate(ref sampleRate);
				Console.WriteLine("\nIQBlk Settings:  IQ Bandwidth:" + (iqBandwidth / 1e6) + ", SampleRate:" + (sampleRate / 1e6));

				//===================SETTINGS===============
				//create a file to write the data to.
				System.IO.StreamWriter iqBlockFile = new System.IO.StreamWriter("IQBlock.txt");

				//Prepare buffer for IQ Block.
				Cplx32[] iqdata = null;

				//Begin the IQ block acquisition.
				bool isActive = true;
				int blockCount = 0;
				int waitTimeoutMsec = 1000;//Maximum allowable wait time for each data acquistion.
				int numTimeouts = 3;//Maximum amount of attempts to acquire data if a timeout occurs.
									//Note: the total wait time to acquire data is waitTimeoutMsec x numTimeouts.
				int timeoutCount = 0;//Variable to track the timeouts.

				//In this example, pressing the ENTER key will force a trigger.	
				//Put the device in triggered mode.
				TriggerMode trigmode = TriggerMode.freeRun;
				returnStatus = apiWrapper.TRIG_SetTriggerMode(trigmode);
				//Set the trigger position at 25%.
				returnStatus = apiWrapper.TRIG_SetTriggerPositionPercent(triggerPosition);
				Console.WriteLine("\n(Press ENTER key to force a trigger)");
				Int64 timeSec = 0;
				UInt64 timeNsec = 0;

				while (isActive)
				{
					//Put the device into Run mode before each acquisition.
					returnStatus = apiWrapper.DEVICE_Run();
					//Acquire data.
					returnStatus = apiWrapper.IQBLK_AcquireIQData();
					//Check if the data block is ready.
					bool blockReady = false;
					returnStatus = apiWrapper.IQBLK_WaitForIQDataReady(waitTimeoutMsec, ref blockReady);

					if (blockReady)
					{
						blockCount++;
						//Get IQ Block data.
						int numPtsRtn = 0;
						IQBLK_ACQINFO acqinfo = new IQBLK_ACQINFO();
						returnStatus = apiWrapper.IQBLK_GetIQDataCplx(ref iqdata, ref numPtsRtn, recordLength);
						returnStatus = apiWrapper.IQBLK_GetIQAcqInfo(ref acqinfo);

						//Acquire the timestamp of the last trigger.
						returnStatus = apiWrapper.REFTIME_GetTimeFromTimestamp(acqinfo.triggerTimestamp, ref timeSec, ref timeNsec);
						if (returnStatus == ReturnStatus.noError)
							Console.WriteLine("\nTrigger timestamp (seconds): " + timeSec);

						//Write data block to file.
						for (int n = 0; n < recordLength; n++)
							iqBlockFile.Write(iqdata[n].i + " " + iqdata[n].q);

						iqBlockFile.Write("\n");

						Console.WriteLine("\nBlock generated.");

					}
					else timeoutCount++;

					//Check if the defined limit of blocks to write has been reached or if the wait time is exceeded.
					if (numAcqs > 0 && blockCount == numAcqs || timeoutCount == numTimeouts)
						isActive = false;
					Console.WriteLine("Is Active?" + isActive);
				}
				//If a ENTER is pressed, a trigger is activated.
				ConsoleKeyInfo keyinfo;
				keyinfo = Console.ReadKey();
				if (keyinfo.Key == ConsoleKey.Enter)
				{
					Console.WriteLine("\nTrigger activated");
					apiWrapper.TRIG_ForceTrigger();
				}
			}

			//Disconnect the device and finish up.
			returnStatus = apiWrapper.DEVICE_Stop();
			returnStatus = apiWrapper.DEVICE_Disconnect();

			Console.WriteLine("\nIQ block acquisition routine complete.");
			Console.WriteLine("\nPress enter key to exit...");
			Console.ReadKey();
		}
		private static void AssignMeasurementVariables(Measurement measurement)
		{
			//detectorType = DetectorTypeShortName2Enum(measurement.DetectorType);
			//windowType = WindowTypeShortName2Enum(measurement.WindowType);
			//spectrumTraceNumber = SpectrumTraceNumberShortName2Enum(measurement.SpectrumTraceNumber);
			//verticalUnits = VerticalUnitsShortName2Enum(measurement.VerticalUnits);
			enableExtRef = measurement.EnableExternalFrequencyRefererence;
			recordLength = measurement.RecordLength;
			numAcqs = measurement.NumberOfAcquisitions;
			centerFreq = measurement.CenterFrequency;
			iqBandwidth = measurement.IQBandwidth;
			refLevel = measurement.ReferenceLevel;
			triggerPosition = measurement.TriggerPosition;
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
	}

}

