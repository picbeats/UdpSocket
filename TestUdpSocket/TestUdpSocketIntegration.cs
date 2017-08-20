using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using UdpSockets;

namespace Test.UdpSockets
{
    [TestFixture]
    internal class TestUdpSocketIntegration
    {
        private static readonly Random Random = new Random();

        private delegate Task EchoTest(IEnumerable<IUdpSocket> client, IPEndPoint serverEndPoint);

        private static void RunTest(EchoTest test, int clientCount)
        {
            var anyPort = new IPEndPoint(IPAddress.Any, 0);

            using (var server = new UdpSocket(new IPEndPoint(IPAddress.Any, 65314), anyPort))
            {
                server.DatagramReceived += (sender, paket) =>
                    ((UdpSocket) sender).Send(paket);

                var serverEndPoint = new IPEndPoint(IPAddress.Loopback, server.Connect().Port);

                var clients = new List<IUdpSocket>();

                try
                {
                    for (var i = 0; i < clientCount; i++)
                    {
                        var client = new global::UdpSockets.UdpSocket(anyPort, serverEndPoint);
                        clients.Add(client);
                        client.Connect();
                    }

                    test(clients, serverEndPoint);
                }
                finally
                {
                    clients.ForEach(c => c.Dispose());
                }
            }
        }

        private static EchoTest EchoRequests(int count)
        {
            return delegate(IEnumerable<IUdpSocket> clients, IPEndPoint serverEndPoint)
            {
                var clientTasks = clients.Select(client => RunTest(client, serverEndPoint, count)).ToList();
                return Task.WhenAll(clientTasks);
            };
        }

        private static async Task RunTest(IUdpSocket client, IPEndPoint serverEndPoint, int count)
        {
            var incomingPakets = new List<UdpDatagram>();

            var receiver = new TaskCompletionSource<object>();

            EventHandler<UdpDatagram> clientOnPaketReceived = (sender, paket) =>
            {
                incomingPakets.Add(paket);

                if (incomingPakets.Count == count)
                    receiver.TrySetResult(null);
            };

            client.DatagramReceived += clientOnPaketReceived;

            try
            {
                var outgoingUdpPakets = new List<UdpDatagram>();

                for (var i = 0; i < count; i++)
                {
                    var udpPaket = CreateUdpPaket(serverEndPoint);
                    client.Send(udpPaket);
                    outgoingUdpPakets.Add(udpPaket);
                }

                await receiver.Task;

                foreach (var pair in outgoingUdpPakets.Zip(incomingPakets, (o, i) => new {Out = o, In = i}))
                {
                    var incomingUdpPaket = pair.In;
                    var outgoingUdpPaket = pair.Out;
                    Assert.That(incomingUdpPaket.RemoteEndPoint, Is.EqualTo(outgoingUdpPaket.RemoteEndPoint));
                    Assert.That(incomingUdpPaket.Data, Is.EquivalentTo(outgoingUdpPaket.Data));
                }
            }
            finally
            {
                client.DatagramReceived -= clientOnPaketReceived;
            }
        }

        private static UdpDatagram CreateUdpPaket(IPEndPoint serverEndPoint)
        {
            var data = new byte[8];
            Random.NextBytes(data);

            var outgoingUdpPaket = new UdpDatagram(serverEndPoint, data);
            return outgoingUdpPaket;
        }

        [Test]
        public void TestEcho()
        {
            RunTest(EchoRequests(1), 1);
        }

        [Test]
        public void TestEchoMany()
        {
            RunTest(EchoRequests(10), 20);
        }
    }
}