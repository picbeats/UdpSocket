using System;

namespace UdpSockets
{
    public interface IUdpSocket : IDisposable
    {
        void Send(UdpDatagram datagram);
        event EventHandler<UdpDatagram> DatagramReceived;
    }
}