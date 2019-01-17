using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO;
using System.Threading;
using ServiceWire.DuplexPipes;

namespace ServiceWire.TcpIp
{
    public class TcpChannel : StreamingChannel
    {
        private Socket _client;
        private string _username;
        private string _password;

        /// <summary>
        /// Creates a connection to the concrete object handling method calls on the server side
        /// </summary>
        /// <param name="serviceType"></param>
        /// <param name="endpoint"></param>
        /// <param name="serializer">Inject your own serializer for complex objects and avoid using the Newtonsoft JSON DefaultSerializer.</param>
        public TcpChannel(Type serviceType, IPEndPoint endpoint, ISerializer serializer)
        {
            Initialize(null, null, serviceType, endpoint, 2500, serializer);
        }

        /// <summary>
        /// Creates a connection to the concrete object handling method calls on the server side
        /// </summary>
        /// <param name="serviceType"></param>
        /// <param name="endpoint"></param>
        /// <param name="serializer">Inject your own serializer for complex objects and avoid using the Newtonsoft JSON DefaultSerializer.</param>
        public TcpChannel(Type serviceType, TcpEndPoint endpoint, ISerializer serializer)
        {
            Initialize(null, null, serviceType, endpoint.EndPoint, endpoint.ConnectTimeOutMs, serializer);
        }

        /// <summary>
        /// Creates a secure connection to the concrete object handling method calls on the server side
        /// </summary>
        /// <param name="serviceType"></param>
        /// <param name="endpoint"></param>
        /// <param name="serializer">Inject your own serializer for complex objects and avoid using the Newtonsoft JSON DefaultSerializer.</param>
        public TcpChannel(Type serviceType, TcpZkEndPoint endpoint, ISerializer serializer)
        {
            if (endpoint == null) throw new ArgumentNullException("endpoint");
            if (endpoint.Username == null) throw new ArgumentNullException("endpoint.Username");
            if (endpoint.Password == null) throw new ArgumentNullException("endpoint.Password");
            Initialize(endpoint.Username, endpoint.Password, 
                serviceType, endpoint.EndPoint, endpoint.ConnectTimeOutMs, serializer);
        }

		private void Initialize(string username, string password, 
            Type serviceType, IPEndPoint endpoint, int connectTimeoutMs, ISerializer serializer)
        {
            _username = username;
            _password = password;
            _serviceType = serviceType;
            _client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp); // TcpClient(AddressFamily.InterNetwork);
            _client.LingerState.Enabled = false;
            _serializer = serializer ?? new DefaultSerializer();

            var connected = false;
            var connectEventArgs = new SocketAsyncEventArgs
            {
                RemoteEndPoint = endpoint
            };
            connectEventArgs.Completed += (sender, e) =>
            {
	            connected = true;
            };

            if (_client.ConnectAsync(connectEventArgs))
            {
                //operation pending - (false means completed synchronously)
                while (!connected)
                {
                    if (!SpinWait.SpinUntil(() => connected, connectTimeoutMs))
                    {
                        _client.Dispose();
                        throw new TimeoutException("Unable to connect within " + connectTimeoutMs + "ms");
                    }
                }
            }
            if (connectEventArgs.SocketError != SocketError.Success)
            {
                _client.Dispose();
                throw new SocketException((int)connectEventArgs.SocketError);
            }
            if (!_client.Connected)
            {
                _client.Dispose();
                throw new SocketException((int)SocketError.NotConnected);
            }

            if (IsPipelines)
            {
	            _duplexPipe = new DuplexPipe(new NetworkStream(_client));
                _duplexPipe.Start();
            }
            else
            {
	            _stream = new BufferedStream(new NetworkStream(_client), 8192);
	            _binReader = new BinaryReader(_stream);
	            _binWriter = new BinaryWriter(_stream);
			}

            try
			{
				if (IsPipelines)
				{
					SyncInterfaceAsync(_serviceType, _username, _password);
				}
				else
				{

					SyncInterface(_serviceType, _username, _password);
				}
            }
            catch (Exception e)
            {
                this.Dispose(true);
                throw;
            }
        }

        public override bool IsConnected => _client?.Connected == true;

		#region IDisposable override

		protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                _duplexPipe?.Dispose();
                _client.Dispose();
            }
        }

        #endregion
    }
}
