﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
//using J2534;
using J2534DotNet;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Threading;
using static Helpers;
using static Upatcher;
using System.Windows.Forms;

namespace UniversalPatcher
{
    /// <summary>
    /// This class encapsulates all code that is unique to the J2534 interface.
    /// </summary>
    ///
    class J2534Device : Device
    {
        /// <summary>
        /// Configuration settings
        /// </summary>
        public int ReadTimeout = 500;
        public int WriteTimeout = 500;

        /// <summary>
        /// variety of properties used to id channels, fitlers and status
        /// </summary>
        private J2534_Struct J2534Port;
        public List<ulong> Filters;
        private int DeviceID;
        private int ChannelID;
        private ProtocolID Protocol;
        public bool IsProtocolOpen;
        private const string PortName = "J2534";
        public string ToolName = "";

        /// <summary>
        /// global error variable for reading/writing. (Could be done on the fly)
        /// TODO, keep record of all errors for debug
        /// </summary>
        public J2534Err OBDError;

        /// <summary>
        /// J2534 has two parts.
        /// J2534device which has the supported protocols ect as indicated by dll and registry.
        /// J2534extended which is al the actual commands and functions to be used. 
        /// </summary>
        struct J2534_Struct
        {
            //public J2534Extended Functions;
            public J2534DotNet.J2534Device LoadedDevice;
            public J2534 Functions;
        }

        public J2534Device(J2534DotNet.J2534Device jport) : base()
        {
            J2534Port = new J2534_Struct();
            //J2534Port.Functions = new J2534Extended();
            J2534Port.Functions = new J2534();
            J2534Port.LoadedDevice = jport;

            // Reduced from 4096+12 for the MDI2
            this.MaxSendSize = 2048 + 12;    // J2534 Standard is 4KB
            this.MaxReceiveSize = 2048 + 12; // J2534 Standard is 4KB
            this.Supports4X = true;
            this.LogDeviceType = DataLogger.LoggingDevType.Other;
        }

        protected override void Dispose(bool disposing)
        {
            DisconnectTool();
        }

        public override string ToString()
        {
            return "J2534 Device";
        }

        // This needs to return Task<bool> for consistency with the Device base class.
        // However it doesn't do anything asynchronous, so to make the code more readable
        // it just wraps a private method that does the real work and returns a bool.
        public override bool Initialize(int BaudRate) //Baudrate not really used
        {
            return this.InitializeInternal();
        }

        // This returns 'bool' for the sake of readability. That bool needs to be
        // wrapped in a Task object for the public Initialize method.
        private bool InitializeInternal()
        {
            Filters = new List<ulong>();

            Logger("Initializing " + this.ToString());

            Response<J2534Err> m; // hold returned messages for processing
            Response<bool> m2;
            Response<double> volts;

            //Check J2534 API
            //Debug.WriteLine(J2534Port.Functions.ToString());

            //Check not already loaded
            if (IsLoaded == true)
            {
                Debug.WriteLine("DLL already loaded, unloading before proceeding");
                m2 = CloseLibrary();
                if (m2.Status != ResponseStatus.Success)
                {
                    Logger("Error closing loaded DLL");
                    return false;
                }
                Debug.WriteLine("Existing DLL successfully unloaded.");
            }

            //Connect to requested DLL
            m2 = LoadLibrary(J2534Port.LoadedDevice);
            if (m2.Status != ResponseStatus.Success)
            {
                Logger("Unable to load the J2534 DLL.");
                return false;
            }
            Debug.WriteLine("Loaded DLL");
            //connect to scantool
            m = ConnectTool();
            if (m.Status != ResponseStatus.Success)
            {
                Logger("Unable to connect to the device: " + m.Value);
                return false;
            }

            Debug.WriteLine("Connected to the device.");

            //Optional.. read API,firmware version ect here
            //read voltage
            volts = ReadVoltage();
            if (volts.Status != ResponseStatus.Success)
            {
                Logger("Unable to read battery voltage.");
            }
            else
            {
                Logger("Battery Voltage is: " + volts.Value.ToString());
            }


            //Set Protocol
            m = ConnectToProtocol(ProtocolID.J1850VPW, BaudRate.J1850VPW_10400, ConnectFlag.NONE);
            if (m.Status != ResponseStatus.Success)
            {
                Logger("Failed to set protocol, J2534 error code: 0x" + m.Value.ToString("X"));
                return false;
            }
            Debug.WriteLine("Protocol Set");
            J2534FunctionsIsLoaded = true;

            SetLoggingFilter();
            //SetAnalyzerFilter();
            Logger("Device initialization complete.");
            return true;
        }

        /// <summary>
        /// Not yet implemented.
        /// </summary>
        public override TimeoutScenario SetTimeout(TimeoutScenario scenario)
        {
            return this.currentTimeoutScenario;
        }

        /// <summary>
        /// This will process incoming messages for up to 500ms looking for a message
        /// </summary>
        public Response<OBDMessage> FindResponse(OBDMessage expected)
        {
            //Debug.WriteLine("FindResponse called");
            for (int iterations = 0; iterations < 5; iterations++)
            {
                OBDMessage response = this.ReceiveMessage();
                    if (Utility.CompareArraysPart(response.GetBytes(), expected.GetBytes()))
                        return Response.Create(ResponseStatus.Success, response);
                Thread.Sleep(100);
            }

            return Response.Create(ResponseStatus.Timeout, (OBDMessage)null);
        }

        /// <summary>
        /// Read an network packet from the interface, and return a Response/Message
        /// </summary>
        public override void Receive()
        {
            //Debug.WriteLine("Trace: Read Network Packet");

            int NumMessages = 1;
            List<PassThruMsg> rxMsgs = new List<PassThruMsg>();
            J2534Err jErr = J2534Port.Functions.ReadMsgs((int)ChannelID, ref rxMsgs, ref NumMessages, ReadTimeout);

            if (jErr == J2534Err.STATUS_NOERROR)
            {
                for (int m = 0; m < rxMsgs.Count; m++)
                {
                    Debug.WriteLine("RX: " + rxMsgs[m].Data.ToHex()); //Debug messages hang program, maybe too frequent?
                    this.Enqueue(new OBDMessage(rxMsgs[m].Data, (ulong)rxMsgs[m].Timestamp, (ulong)OBDError));
                }
            }
            else
            {
                Debug.WriteLine("Receive error: " + jErr);
            }
        }

        /// <summary>
        /// Send a message, wait for a response, return the response.
        /// </summary>
        public override bool SendMessage(OBDMessage message, int responses)
        {
            //Debug.WriteLine("Send request called");
            //Debug.WriteLine("TX: " + message.GetBytes().ToHex()); //Too much debugging?
            PassThruMsg TempMsg = new PassThruMsg(Protocol, TxFlag.NONE, message.GetBytes());
            int NumMsgs = 1;

            DataLogger.LogDevice.MessageSent(message);

            OBDError = J2534Port.Functions.WriteMsgs((int)ChannelID, TempMsg, ref NumMsgs, WriteTimeout);
            if (OBDError != J2534Err.STATUS_NOERROR)
            {
                //Debug messages here...check why failed..
                string errMsg = "";
                J2534Port.Functions.GetLastError(ref errMsg);
                Debug.WriteLine("J2534 WriteMsgs error: " + errMsg);
                return false;
            }
            //Debug.WriteLine("J2534 WriteMsgs sent: " + NumMsgs);
            return true;
        }
        
        /// <summary>
        /// load in dll
        /// </summary>
        private Response<bool> LoadLibrary(J2534DotNet.J2534Device TempDevice)
        {
            ToolName = TempDevice.Name;
            J2534Port.LoadedDevice = TempDevice;
            if (J2534Port.Functions.LoadLibrary(J2534Port.LoadedDevice))
            {
                return Response.Create(ResponseStatus.Success, true);
            }
            else
            {
                return Response.Create(ResponseStatus.Error, false);
            }
        }

        /// <summary>
        /// unload dll
        /// </summary>
        private Response<bool> CloseLibrary()
        {
            if (J2534Port.Functions.FreeLibrary())
            {
                return Response.Create(ResponseStatus.Success, true);
            }
            else
            {
                return Response.Create(ResponseStatus.Error, false);
            }
        }

        /// <summary>
        /// Connects to physical scantool
        /// </summary>
        private Response<J2534Err> ConnectTool()
        {
            DeviceID = 0;
            ChannelID = 0;
            Filters.Clear();
            OBDError = 0;
            int tmpId = 0;
            OBDError = J2534Port.Functions.Open(ref tmpId);
            if (OBDError != J2534Err.STATUS_NOERROR)
            {
                return Response.Create(ResponseStatus.Error, OBDError);
            }
            else
            {
                DeviceID = tmpId;
                return Response.Create(ResponseStatus.Success, OBDError);
            }
        }

        /// <summary>
        /// Disconnects from physical scantool
        /// </summary>
        private Response<J2534Err> DisconnectTool()
        {
            if (!J2534FunctionsIsLoaded)
            {
                Response.Create(ResponseStatus.Success, OBDError);
            }
            OBDError = J2534Port.Functions.Disconnect((int)ChannelID);
            if (OBDError != J2534Err.STATUS_NOERROR)
            {
                Debug.WriteLine("J2534 Disconnect: " + OBDError.ToString());
            }
            OBDError = J2534Port.Functions.Close((int)DeviceID);
            if (OBDError != J2534Err.STATUS_NOERROR)
            {
                Debug.WriteLine("J2534 Close: " + OBDError.ToString());
            }
            J2534Port.Functions.FreeLibrary();
            return Response.Create(ResponseStatus.Success, OBDError);
        }

        /// <summary>
        /// keep record if DLL has been loaded in
        /// </summary>
        public bool IsLoaded
        {
            get 
            {
                if (J2534Port.Functions == null)
                    return false;
                return J2534Port.Functions.isDllLoaded(); 
            }
        }

        /// <summary>
        /// connect to selected protocol
        /// Must provide protocol, speed, connection flags, recommended optional is pins
        /// </summary>
        private Response<J2534Err> ConnectToProtocol(ProtocolID ReqProtocol, BaudRate Speed, ConnectFlag ConnectFlags)
        {
            int tmpChannel = 0;
            OBDError = J2534Port.Functions.Connect((int)DeviceID, ReqProtocol,  ConnectFlags,  Speed, ref tmpChannel);
            if (OBDError != J2534Err.STATUS_NOERROR) return Response.Create(ResponseStatus.Error, OBDError);
            ChannelID = tmpChannel;
            Protocol = ReqProtocol;
            IsProtocolOpen = true;
            return Response.Create(ResponseStatus.Success, OBDError);
        }

        /// <summary>
        /// Disconnect from protocol
        /// </summary>
        private Response<J2534Err> DisconnectFromProtocol()
        {
            OBDError = J2534Port.Functions.Disconnect((int)ChannelID);
            if (OBDError != J2534Err.STATUS_NOERROR) return Response.Create(ResponseStatus.Error, OBDError);
            IsProtocolOpen = false;
            return Response.Create(ResponseStatus.Success, OBDError);
        }

        /// <summary>
        /// Read battery voltage
        /// </summary>
        public Response<double> ReadVoltage()
        {
            double Volts = 0;
            int VoltsAsInt = 0;
            OBDError = J2534Port.Functions.ReadBatteryVoltage((int)DeviceID, ref VoltsAsInt);
            if (OBDError != J2534Err.STATUS_NOERROR)
            {
                return Response.Create(ResponseStatus.Error, Volts);
            }
            else
            {
                Volts = VoltsAsInt / 1000.0;
                return Response.Create(ResponseStatus.Success, Volts);
            }
        }

        /// <summary>
        /// Set filter
        /// </summary>
        private Response<J2534Err> SetFilter(UInt32 Mask,UInt32 Pattern,UInt32 FlowControl,TxFlag txflag,FilterType Filtertype)
        {
            PassThruMsg maskMsg;
            PassThruMsg patternMsg;
            PassThruMsg FlowMsg = new PassThruMsg();

            maskMsg = new PassThruMsg(Protocol, txflag, new Byte[] { (byte)(0xFF & (Mask >> 16)), (byte)(0xFF & (Mask >> 8)), (byte)(0xFF & Mask) });
            patternMsg = new PassThruMsg(Protocol, txflag, new Byte[] { (byte)(0xFF & (Pattern >> 16)), (byte)(0xFF & (Pattern >> 8)), (byte)(0xFF & Pattern) });

            int tempfilter = 0;
            //OBDError = J2534Port.Functions.StartMsgFilter((int)ChannelID, Filtertype, ref maskMsg, ref patternMsg, ref FlowMsg, ref tempfilter);
            OBDError = J2534Port.Functions.StartMsgFilter((int)ChannelID, Filtertype, maskMsg, patternMsg, ref tempfilter);

            if (OBDError != J2534Err.STATUS_NOERROR) return Response.Create(ResponseStatus.Error, OBDError);
            Filters.Add((ulong)tempfilter);
            return Response.Create(ResponseStatus.Success, OBDError);
        }

        /// <summary>
        /// Clear filters
        /// </summary>
        private Response<J2534Err> ClearFilters()
        {
            /*            for (int i = 0; i < Filters.Count; i++)
                        {
                            OBDError = J2534Port.Functions.StopMsgFilter((int)ChannelID, (int)Filters[i]);
                            if (OBDError != J2534Err.STATUS_NOERROR)
                            {
                                Response.Create(ResponseStatus.Error, OBDError);
                            }
                        }
            */
            OBDError = J2534Port.Functions.ClearMsgFilters(ChannelID);
            Filters.Clear();
            return Response.Create(ResponseStatus.Success, OBDError);
        }

        /// <summary>
        /// Set the interface to low (false) or high (true) speed
        /// </summary>
        /// <remarks>
        /// The caller must also tell the PCM to switch speeds
        /// </remarks>
        protected override bool SetVpwSpeedInternal(VpwSpeed newSpeed)
        {
            if (newSpeed == VpwSpeed.Standard)
            {
                Debug.WriteLine("J2534 setting VPW 1X");
                //Disconnect from current protocol
                DisconnectFromProtocol();

                //Connect at new speed
                ConnectToProtocol(ProtocolID.J1850VPW, BaudRate.J1850VPW_10400, ConnectFlag.NONE);

                //Set Filter
                SetFilter(0xFEFFFF, 0x6CF010, 0, TxFlag.NONE, FilterType.PASS_FILTER);
                //if (m.Status != ResponseStatus.Success)
                //{
                //    Debug.WriteLine("Failed to set filter, J2534 error code: 0x" + m.Value.ToString("X2"));
                //    return false;
                //}


            }
            else
            {
                Debug.WriteLine("J2534 setting VPW 4X");
                //Disconnect from current protocol
                DisconnectFromProtocol();

                //Connect at new speed
                ConnectToProtocol(ProtocolID.J1850VPW, BaudRate.J1850VPW_41600, ConnectFlag.NONE);

                //Set Filter
                SetFilter(0xFEFFFF, 0x6CF010, 0, TxFlag.NONE, FilterType.PASS_FILTER);

            }

            return true;
        }

        public override void ClearMessageBuffer()
        {
            J2534Port.Functions.ClearRxBuffer((int)ChannelID);
            J2534Port.Functions.ClearTxBuffer((int)ChannelID);
        }

        public override bool SetLoggingFilter()
        {
            if (this.CurrentFilter == "logging")
                return true;
            if (DataLogger.useBusFilters == false)
            {
                RemoveFilters();    
                this.CurrentFilter = "logging";
                return true;
            }

            Debug.WriteLine("Set logging filter");
            ClearFilters();
            //Response<J2534Err> m = SetFilter(0xFEFFFF, 0x6CF010, 0, TxFlag.NONE, FilterType.PASS_FILTER);
            Response<J2534Err> m = SetFilter(0xFF00, 0xF000, 0, TxFlag.NONE, FilterType.PASS_FILTER);
            if (m.Status != ResponseStatus.Success)
            {
                Debug.WriteLine("Failed to set filter, J2534 error code: 0x" + m.Value.ToString("X2"));
                return false;
            }
            Debug.WriteLine("Filter set");
/*            m = SetFilter(0xFEFFFF, 0x8CF010, 0, TxFlag.NONE, FilterType.PASS_FILTER);
            if (m.Status != ResponseStatus.Success)
            {
                Debug.WriteLine("Failed to set filter, J2534 error code: 0x" + m.Value.ToString("X2"));
                return false;
            }
            Debug.WriteLine("Filter 2 set");
*/            this.CurrentFilter = "logging";

            return true;

        }

        public override bool SetAnalyzerFilter()
        {
            if (this.CurrentFilter == "analyzer")
                return true;
            Debug.WriteLine("Setting analyzer filter");

            ClearFilters();
            Response<J2534Err> m = SetFilter(0x0, 0x0, 0, TxFlag.NONE, FilterType.PASS_FILTER);
            if (m.Status != ResponseStatus.Success)
            {
                Debug.WriteLine("Failed to set filter, J2534 error code: 0x" + m.Value.ToString("X2"));
                return false;
            }
            Debug.WriteLine("Filter set");
            this.CurrentFilter = "analyzer";
            return true;
        }

        public override bool RemoveFilters()
        {
            Debug.WriteLine("Removing filters");
            ClearFilters();
            Response<J2534Err> m = SetFilter(0x0, 0x0, 0, TxFlag.NONE, FilterType.PASS_FILTER);
            if (m.Status != ResponseStatus.Success)
            {
                Debug.WriteLine("Failed to set filter, J2534 error code: 0x" + m.Value.ToString("X2"));
                return false;
            }
            Debug.WriteLine("Filter set");
            return true;
        }

    }
}
