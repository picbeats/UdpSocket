using System.Net;

namespace UdpSockets
{
    public class UdpDatagram
    {
        public UdpDatagram(IPEndPoint remoteEndPoint, byte[] data)
        {
            RemoteEndPoint = remoteEndPoint;
            Data = data;
        }

        public IPEndPoint RemoteEndPoint { get; }
        public byte[] Data { get; }

        public override string ToString()
        {
            return $"{nameof(RemoteEndPoint)}: {RemoteEndPoint}, {nameof(Data)}: {Data}";
        }
    }
}