using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace UdpSockets
{
    public sealed class UdpSocket : IUdpSocket
    {
        // const long IOC_IN =     0x80000000;
        // const long IOC_VENDOR = 0x18000000;
        // const long SIO_UDP_CONNRESET = IOC_IN | IOC_VENDOR | 12;        
        private const int SioUdpConnreset = -1744830452;
        private static readonly byte[] SioUdpConnResetDisabled = {0};

        public int BufferSize { get; }
        private Socket socket;
        private SocketAsyncEventArgs asyncReadEventArgs;
        private SocketAsyncEventArgs asyncSendEventArgs;
        private readonly Queue<UdpDatagram> sendQueue = new Queue<UdpDatagram>();
        private volatile bool sending;
        private volatile bool terminated;

        public IPEndPoint BindToEndPoint { get; }
        public IPEndPoint ReceiveFromEndPoint { get; }
        public IPEndPoint LocalEndPoint { get; private set; } 

        public UdpSocket(IPEndPoint bindToEndPoint, IPEndPoint receiveFromEndPoint, int bufferSize = 4096)
        {
            BufferSize = bufferSize;
            BindToEndPoint = bindToEndPoint;
            ReceiveFromEndPoint = receiveFromEndPoint;
        }

        public IPEndPoint Connect()
        {
            if (terminated)
                throw new InvalidOperationException("already terminated");

            if (socket != null)
                throw new InvalidOperationException("already connected");

            CreateAndBindSocket();
            StartReceive();
            LocalEndPoint = socket.LocalEndPoint as IPEndPoint;
            return LocalEndPoint;
        }

        public void Send(UdpDatagram datagram)
        {
            lock (sendQueue)
            {
                if (terminated)
                    throw new InvalidOperationException("Socket is already terminated");

                if (socket == null)
                    throw new InvalidOperationException("Socket is not connected, call Connect first");                
                
                if (!sending)
                    sending = StartSend(datagram);
                else
                    sendQueue.Enqueue(datagram);
            }
        }

        public event EventHandler<UdpDatagram> DatagramReceived;

        public void Dispose()
        {
            lock (sendQueue)
            {
                terminated = true;
                socket?.Close(100);
                socket?.Dispose();
                DisposeAsynceEventArgs(ref asyncReadEventArgs);
                DisposeAsynceEventArgs(ref asyncSendEventArgs);
                DatagramReceived = null;
                socket = null;
            }
        }

        private void CreateAndBindSocket()
        {
            var addressFamily = BindToEndPoint.AddressFamily;
            
            socket = new Socket(addressFamily, SocketType.Dgram, ProtocolType.Udp);
            
            if (addressFamily == AddressFamily.InterNetworkV6)
                socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, true);
            
            // ignore ICMP "port unreachable" messages when sending an UDPdatagram
            if (IsWindowsPlatform())
                socket.IOControl(SioUdpConnreset, SioUdpConnResetDisabled, null);
            
            socket.Bind(BindToEndPoint);
        }

        private static bool IsWindowsPlatform()
        {
            var osVersionPlatform = Environment.OSVersion.Platform;
            
            switch (osVersionPlatform)
            {
                case PlatformID.Win32S:
                    return true;
                case PlatformID.Win32Windows:
                    return true;
                case PlatformID.Win32NT:
                    return true;
                case PlatformID.WinCE:
                    return true;
                case PlatformID.Xbox:
                    return true;
                case PlatformID.Unix:
                    return false;
                case PlatformID.MacOSX:
                    return false;
                default:
                    return true;
            }
        }

        private void DisposeAsynceEventArgs(ref SocketAsyncEventArgs socketAsyncEventArgs)
        {
            if (socketAsyncEventArgs == null)
                return;

            socketAsyncEventArgs.Completed -= SendCallback;
            socketAsyncEventArgs.Dispose();
            socketAsyncEventArgs = null;
        }

        private void StartReceive()
        {
            asyncReadEventArgs = new SocketAsyncEventArgs
            {
                RemoteEndPoint = ReceiveFromEndPoint
            };

            asyncReadEventArgs.Completed += ReceiveCallback;
            asyncReadEventArgs.SetBuffer(new byte[BufferSize], 0, BufferSize);

            if (!socket.ReceiveFromAsync(asyncReadEventArgs))
                ReceiveCallback(socket, asyncReadEventArgs);
        }

        private bool StartSend(UdpDatagram paket)
        {
            if (asyncSendEventArgs == null)
            {
                asyncSendEventArgs = new SocketAsyncEventArgs();
                asyncSendEventArgs.Completed += SendCallback;
            }

            asyncSendEventArgs.RemoteEndPoint = paket.RemoteEndPoint;
            var paketData = paket.Data;
            asyncSendEventArgs.SetBuffer(paketData, 0, paketData.Length);
            return socket?.SendToAsync(asyncSendEventArgs) ?? true;
        }

        private void ReceiveCallback(object sender, SocketAsyncEventArgs e)
        {
            bool repeat;

            do
            {
                var data = GetReceivedData(e);

                if (data == null)
                    return;

                var datagram = new UdpDatagram(e.RemoteEndPoint as IPEndPoint, data);

                OnPaketReceived(datagram);

                repeat = !terminated &&
                         !(socket?.ReceiveFromAsync(e) ?? true);
            } while (repeat);
        }

        private void SendCallback(object sender, SocketAsyncEventArgs e)
        {
            lock (sendQueue)
            {
                sending = false;

                while (!sending && sendQueue.Count > 0)
                {
                    var datagram = sendQueue.Dequeue();
                    sending = StartSend(datagram);
                }
            }
        }

        private static byte[] GetReceivedData(SocketAsyncEventArgs e)
        {
            var count = e.BytesTransferred;
            if (count == 0) return null;
            var data = new byte[count];
            Buffer.BlockCopy(e.Buffer, 0, data, 0, count);
            return data;
        }

        private void OnPaketReceived(UdpDatagram e)
        {
            DatagramReceived?.Invoke(this, e);
        }
    }
}