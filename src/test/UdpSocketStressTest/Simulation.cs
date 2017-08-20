using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using UdpSockets;

namespace Test.UdpSockets.MemoryUsage
{
    internal class Simulation
    {
        private static readonly Random Random = new Random();
        private static readonly IPEndPoint AnyPort = new IPEndPoint(IPAddress.Any, 0);
        private static readonly byte[] PerformanceTestDatagramData = new byte[100];
        private readonly CancellationToken cancellationToken;

        private delegate Task ClientWithEchoServerTest(IUdpSocket client, IPEndPoint serverEndPoint);
        private delegate Task ServerTest(IPEndPoint serverEndPoint);
        private delegate UdpDatagram CreateDatagram(IPEndPoint serverEndPoint);

        public Simulation(CancellationToken tokenSourceToken)
        {
            cancellationToken = tokenSourceToken;
        }

        public async Task Run(int clientCount, int parallelity, int requestsPerSession)
        {
            var elapsed = await Measure(() => RunWithServer(WithManySequentialClients(clientCount, parallelity, PingPongTest(requestsPerSession, WithPerformanceTestDatagram))));
            Console.WriteLine($"Running {clientCount} with {requestsPerSession} request maximum {parallelity} in parallel finished in {elapsed.TotalMilliseconds:F0} ms, effective as {clientCount/elapsed.TotalSeconds:F1} sessions per second");
        }

        private static async Task<TimeSpan> Measure(Func<Task> task)
        {
            var sw = Stopwatch.StartNew();
            await task();
            sw.Stop();
            return sw.Elapsed;
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
                    Debug.Assert(task.IsCompleted);
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
                    var incomingUdpPaket = await bufferBlock.ReceiveAsync(cancellationToken);
                    Debug.Assert(incomingUdpPaket.RemoteEndPoint.Equals(outgoingUdpPaket.RemoteEndPoint));
                    Debug.Assert(incomingUdpPaket.Data.SequenceEqual(outgoingUdpPaket.Data));
                }
            }
            finally
            {
                client.DatagramReceived -= clientOnPaketReceived;
            }
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