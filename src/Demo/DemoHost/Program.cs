using DemoCommon;
using ServiceWire;
using ServiceWire.TcpIp;
using ServiceWire.ZeroKnowledge;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace DemoHost
{
    class Program
    {
        static void Main(string[] args)
        {
            var logger = new Logger(logLevel: LogLevel.Debug, options: LogOptions.LogOnlyToConsole, messageBufferSize:1);
            var stats = new Stats();

            var addr = new[] { "127.0.0.1", "8099" }; //defaults
            if (null != args && args.Length > 0)
            {
                var parts = args[0].Split(':');
                if (parts.Length > 1) addr[1] = parts[1];
                addr[0] = parts[0];
            }

            var ip = addr[0];
            var port = Convert.ToInt32(addr[1]);
            var ipEndpoint = new IPEndPoint(IPAddress.Any, port);

            var useCompression = false;
            var compressionThreshold = 131072; //128KB
            var zkRepository = new DemoZkRepository();

            var tcphost = new TcpHost(ipEndpoint, logger, stats, zkRepository);
            tcphost.UseCompression = useCompression;
            tcphost.CompressionThreshold = compressionThreshold;

			var simpleContract = new DataContractImpl();
			tcphost.AddService<IDataContract>(simpleContract);

			var complexContract = new ComplexDataContractImpl();
			tcphost.AddService<IComplexDataContract>(complexContract);

			var test = new Test();
			tcphost.AddService<ITest>(test);
            tcphost.IsPipeline = false;
            tcphost.Open();

            var tcphostPipeline = new TcpHost(new IPEndPoint(IPAddress.Any, 8088), logger, stats, zkRepository)
            {
                UseCompression = useCompression,
                CompressionThreshold = compressionThreshold
            };

            tcphostPipeline.AddService<IDataContract>(new DataContractImpl());

            tcphostPipeline.AddService<IComplexDataContract>(new ComplexDataContractImpl());

            tcphostPipeline.AddService<ITest>(new Test());
            tcphostPipeline.IsPipeline = true;
            tcphostPipeline.Open();


            Console.WriteLine("Press Enter to stop the dual host test.");
            Console.ReadLine();

            tcphostPipeline.Close();

            Console.WriteLine("Press Enter to quit.");
            Console.ReadLine();
        }
    }

    public class Test : ITest
    {
	    public Task SetAsync(int a)
	    {
			return Task.CompletedTask;
		}

        Task<string[]> ITest.GetAsync()
        {
            var list = Enumerable.Range(0, 50).Select(x => Guid.NewGuid().ToString("D")).ToArray();
            return Task.FromResult(list);
        }

        public string[] GetItems(Guid id)
        {
            var list = Enumerable.Range(0, 50).Select(x => Guid.NewGuid().ToString("D")).ToArray();
            return list;
        }

        public Task<int> GetAsync()
	    {
			return Task.FromResult(1);
		}
    }

    public class DataContractImpl : IDataContract
    {
        public decimal GetDecimal(decimal input)
        {
            return input += 456.44m;
        }

        public bool OutDecimal(decimal val)
        {
            val = 45.66m;
            return true;
        }
    }

    public class ComplexDataContractImpl : IComplexDataContract
    {
        public Guid GetId(string source, double weight, int quantity, DateTime dt)
        {
            return Guid.NewGuid();
        }

        public ComplexResponse Get(Guid id, string label, double weight, out long quantity)
        {
            quantity = 42;
            return new ComplexResponse { Id = id, Label = "Hello, world.", Quantity = quantity };
        }

        public List<string> GetItems(Guid id)
        {
            var list = new List<string>();
            list.Add("42");
            list.Add(id.ToString());
            list.Add("Test");
            return list;
        }

        public long TestLong(out long id1, out long id2)
        {
            id1 = 23;
            id2 = 24;
            return 25;
        }
    }

    public class DemoZkRepository : IZkRepository
    {
        private string password = "password";
        private ZkProtocol _protocol = new ZkProtocol();
        private ZkPasswordHash _hash = null;

        public ZkPasswordHash GetPasswordHashSet(string username)
        {
            if (_hash == null) _hash = _protocol.HashCredentials(username, password);
            return _hash;
        }
    }
}
