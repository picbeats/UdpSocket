using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using NUnit.Framework;
using UdpSockets;

namespace Test.UdpSockets
{
    [TestFixture]
    internal class TestUdpSocketIntegration
    {
        private static readonly Random Random = new Random();
        private static readonly IPEndPoint AnyPort = new IPEndPoint(IPAddress.Any, 0);
        private static readonly byte[] PerformanceTestDatagramData = new byte[100];
        private CancellationTokenSource cancellationTokenSource;

        private delegate Task ClientWithEchoServerTest(IUdpSocket client, IPEndPoint serverEndPoint);
        private delegate Task ServerTest(IPEndPoint serverEndPoint);
        private delegate UdpDatagram CreateDatagram(IPEndPoint serverEndPoint);

        [SetUp]
        public void SetUp()
        {
            cancellationTokenSource = new CancellationTokenSource(10000);
        }

        [Test]
        public Task TestEcho()
        {
            return RunWithServer(WithParallelClients(1, EchoRequests(1, WithRandomDatagram(8))));
        }

        [Test]
        public async Task TestEchoMany()
        {
            const int clientCount = 20;
            const int requestCount = 10;
            var elapsed = await Measure(() => RunWithServer(WithParallelClients(clientCount, EchoRequests(requestCount, WithRandomDatagram(8)))));
            Console.WriteLine($"Running {clientCount} with {requestCount} requests  in parallel finished in {elapsed.TotalMilliseconds:F0} ms, {clientCount*requestCount/elapsed.TotalSeconds:F1} requests per second");
        }

        [Test]
        [Explicit("Performance test")]
        public async Task TestEchoManyFlights()
        {
            const int clientCount = 10;
            const int requestCount = 1000;
            var elapsed = await Measure(() => RunWithServer(WithParallelClients(clientCount, PingPongTest(requestCount, WithPerformanceTestDatagram))));
            Console.WriteLine($"Running {clientCount} in parallel with {requestCount} requests finished in {elapsed.TotalMilliseconds:F0} ms, {clientCount*requestCount/elapsed.TotalSeconds} requests per second");
        }
        
        [Test]
        [Explicit("Performance test")]
        public async Task TestRunManySequential()
        {
            const int clientCount = 200;
            const int batchSize = 50;
            const int requestCount = 3;
            var elapsed = await Measure(() => RunWithServer(WithManySequentialClients(clientCount, batchSize, PingPongTest(requestCount, WithPerformanceTestDatagram))));
            Console.WriteLine($"Running {clientCount} with {requestCount} request maximum {batchSize} in parallel finished in {elapsed.TotalMilliseconds:F0} ms, effective as {clientCount/elapsed.TotalSeconds:F1} requests per second with {requestCount} flights");
        }

        private static async Task<TimeSpan> Measure(Func<Task> task)
        {
            var sw = Stopwatch.StartNew();
            await task();
            sw.Stop();
            return sw.Elapsed;
        }

        private static ServerTest WithParallelClients(int clientCount, ClientWithEchoServerTest test)
        {
            return async delegate(IPEndPoint serverEndPoint)
            {
                var clientsTasks = new List<Task>();

                for (var i = 0; i < clientCount; i++)
                {
                    clientsTasks.Add(RunTestWithNewClient(test, serverEndPoint));
                }

                await Task.WhenAll(clientsTasks);
            };
        }

        private static ServerTest WithManySequentialClients(int clientCount, int batchSize, ClientWithEchoServerTest test)
        {
            return async delegate(IPEndPoint serverEndPoint)
            {
                var clientsTasks = new List<Task>();
                
                var i = 0;
                for (; i < clientCount && i < batchSize; i++)
                {
                    clientsTasks.Add(RunTestWithNewClient(test, serverEndPoint));
                }

                while (clientsTasks.Count > 0)
                {
                    var task = await Task.WhenAny(clientsTasks);
                    Assert.That(task.IsCompleted);
                    clientsTasks.Remove(task);

                    if (i < clientCount)
                    {
                        clientsTasks.Add(RunTestWithNewClient(test, serverEndPoint));
                        i++;
                    }
                }
            };
        }

        private static async Task RunTestWithNewClient(ClientWithEchoServerTest test, IPEndPoint serverEndPoint1)
        {
            using (var client = new UdpSocket(AnyPort, serverEndPoint1))
            {
                client.Connect();
                await test(client, serverEndPoint1);
            }
        }

        private static async Task RunWithServer(ServerTest test)
        {
            using (var server = new UdpSocket(new IPEndPoint(IPAddress.Any, 65314), AnyPort))
            {
                server.DatagramReceived += (sender, paket) =>
                    ((UdpSocket) sender).Send(paket);

                server.Connect();

                var serverEndPoint = new IPEndPoint(IPAddress.Loopback, server.LocalEndPoint.Port);
                await test(serverEndPoint);
            }
        }

        private ClientWithEchoServerTest EchoRequests(int requestCount, CreateDatagram createDatagram) => 
            (client, serverEndPoint) => RunEchoTest(client, serverEndPoint, requestCount, createDatagram);

        private async Task RunEchoTest(IUdpSocket client, IPEndPoint serverEndPoint, int requestCount, CreateDatagram createDatagram)
        {
            var incomingPakets = new List<UdpDatagram>();

            var receiver = new TaskCompletionSource<object>();

            EventHandler<UdpDatagram> clientOnPaketReceived = (sender, paket) =>
            {
                incomingPakets.Add(paket);

                if (incomingPakets.Count == requestCount)
                    receiver.TrySetResult(null);
            };

            client.DatagramReceived += clientOnPaketReceived;

            try
            {
                var outgoingUdpPakets = new List<UdpDatagram>();

                for (var i = 0; i < requestCount; i++)
                {
                    var udpPaket = createDatagram(serverEndPoint);
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

        private ClientWithEchoServerTest PingPongTest(int requestCount, CreateDatagram createDatagram) => 
            (client, serverEndPoint) => RunPingPongTest(client, serverEndPoint, requestCount, createDatagram);
        
        private async Task RunPingPongTest(IUdpSocket client, IPEndPoint serverEndPoint, int requestCount, CreateDatagram createDatagram)
        {
            var bufferBlock = new BufferBlock<UdpDatagram>();

            EventHandler<UdpDatagram> clientOnPaketReceived = (sender, datagram) => bufferBlock.Post(datagram);

            client.DatagramReceived += clientOnPaketReceived;

            try
            {
                for (var i = 0; i < requestCount; i++)
                {
                    var outgoingUdpPaket = createDatagram(serverEndPoint);
                    client.Send(outgoingUdpPaket);
                    var incomingUdpPaket = await bufferBlock.ReceiveAsync(cancellationTokenSource.Token);
                    Assert.That(incomingUdpPaket.RemoteEndPoint, Is.EqualTo(outgoingUdpPaket.RemoteEndPoint));
                    Assert.That(incomingUdpPaket.Data, Is.EquivalentTo(outgoingUdpPaket.Data));
                }
            }
            finally
            {
                client.DatagramReceived -= clientOnPaketReceived;
            }
        }
        

        private static CreateDatagram WithRandomDatagram(int paketSize) =>
            serverEndPoint => CreateRandomDatagram(serverEndPoint, paketSize);

        private static UdpDatagram CreateRandomDatagram(IPEndPoint serverEndPoint, int paketSize)
        {
            var data = new byte[paketSize];
            Random.NextBytes(data);

            var outgoingUdpPaket = new UdpDatagram(serverEndPoint, data);
            return outgoingUdpPaket;
        }
        

        private static UdpDatagram WithPerformanceTestDatagram(IPEndPoint serverEndPoint)
        {
            var data = new byte[100];
            Random.NextBytes(data);
            var outgoingUdpPaket = new UdpDatagram(serverEndPoint, PerformanceTestDatagramData);
            return outgoingUdpPaket;
        }
    }
}