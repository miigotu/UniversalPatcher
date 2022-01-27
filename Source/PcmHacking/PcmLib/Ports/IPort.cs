﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>
    /// The IPort implementations encapsulate the differences between serial 
    /// ports, J2534 passthrough devices, and whatever else we end up using.
    /// </summary>
    public interface IPort : IDisposable
    {
        /// <summary>
        /// Open the port.
        /// </summary>
        void OpenAsync(PortConfiguration configuration);

        /// <summary>
        /// Send a sequence of bytes.
        /// </summary>
        void Send(byte[] buffer);

        /// <summary>
        /// Receive a buffer of bytes.
        /// </summary>
        int Receive(byte[] buffer, int offset, int count);

        /// <summary>
        /// Discard anything in the input and output buffers.
        /// </summary>
        void DiscardBuffers();

        /// <summary>
        /// Indicates the number of bytes waiting in the receive queue.
        /// </summary>
        int GetReceiveQueueSize();

        /// <summary>
        /// Sets the timeout for incoming messages;
        /// </summary>
        void SetTimeout(int milliseconds);
    }

    public class PortConfiguration
    {
    }

    public class SerialPortConfiguration : PortConfiguration
    {
        public int BaudRate { get; set; }
        public int Timeout { get; set; }
    }
}
