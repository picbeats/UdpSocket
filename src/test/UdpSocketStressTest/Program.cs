using System;
using System.Collections.Generic;
using System.Threading;

namespace Test.UdpSockets.MemoryUsage
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            var count = GetArgument(args, 10000, 0);
            var parallelity = GetArgument(args, 50, 1);
            var requestsPerSession = GetArgument(args, 3, 2);

            Console.WriteLine($"Running simulation with {count} clients running a session with {requestsPerSession} ping-pong requests with parallity of {parallelity}");

            var tokenSource = new CancellationTokenSource(100000);
            
            var simulation = new Simulation(tokenSource.Token);

            simulation.Run(count, parallelity, requestsPerSession).Wait(tokenSource.Token);
        }

        private static int GetArgument(IReadOnlyList<string> args, int defaultValue, int index)
        {
            if (args.Count <= index) 
                return defaultValue;
            
            int value;

            if (int.TryParse(args[index], out value) && value > 0)
            {
                return value;
            }

            return defaultValue;
        }
    }
}