using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using ServiceWire.ArrayPoolOwners;
using ServiceWire.DuplexPipes;
using ServiceWire.ZeroKnowledge;

namespace ServiceWire
{
    public abstract class Host : IDisposable
    {
        protected volatile bool _isOpen;
        protected volatile bool _continueListening = true;
        protected bool _useCompression = false; //default is false
        protected int _compressionThreshold = 131072; //128KB
        protected ILog _log = new NullLogger();
        protected IStats _stats = new NullStats();
        protected ISerializer _serializer;
        protected IZkRepository _zkRepository = new ZkNullRepository();
        private volatile bool _requireZk = false;

        protected ConcurrentDictionary<string, int> _serviceKeys = new ConcurrentDictionary<string, int>();
        protected ConcurrentDictionary<int, ServiceInstance> _services = new ConcurrentDictionary<int, ServiceInstance>();
        protected readonly ParameterTransferHelper _parameterTransferHelper;

        public Host()
        {
            _parameterTransferHelper = new ParameterTransferHelper(_serializer);
        }

        public IZkRepository ZkRepository
        {
            get => _zkRepository;
            set
            {
                _zkRepository = value;
                _requireZk = !(_zkRepository is ZkNullRepository);
            }
        }

        public IStats Stats
        {
            get => _stats;
            set => _stats = value ?? _stats;
        }

        public ILog Log
        {
            get => _log;
            set => _log = value ?? _log;
        }

        protected bool Continue
        {
            get => _continueListening;
            set => _continueListening = value;
        }

        /// <summary>
        /// Enable parameter compression. Default is false. There is a performance penalty
        /// when using compression that should be weighed against network transmission
        /// costs of large data parameters being serialized across the wire.
        /// </summary>
        public bool UseCompression
        {
            get => _useCompression;
            set => _useCompression = value;
        }

        /// <summary>
        /// Compression, if enabled, occurs once a parameter exceeds this value
        /// in the number of bytes. Strings, byte and char arrays, and complex serialized types.
        /// The minimum is 1024 bytes. The default is 128KB.
        /// </summary>
        public int CompressionThreshold
        {
            get => _compressionThreshold;
            set
            {
                _compressionThreshold = value;
                if (_compressionThreshold < 1024) _compressionThreshold = 1024;
            }
        }

        /// <summary>
        /// Add this service implementation to the host.
        /// </summary>
        /// <typeparam name="TService"></typeparam>
        /// <param name="service">The singleton implementation.</param>
        public void AddService<TService>(TService service) where TService : class
        {
            try
            {
                if (_isOpen) throw new Exception("Service cannot be added after the host is opened.");
                var serviceType = typeof(TService);
                if (!serviceType.IsInterface) throw new ArgumentException("TService must be an interface.", "TService");
                //serviceType.ValidateServiceInterface(); //throws if one class in the interface or its members is not serializable
                var serviceKey = serviceType.FullName; // serviceType.AssemblyQualifiedName ?? serviceType.FullName;
                if (_serviceKeys.ContainsKey(serviceKey)) throw new Exception("Service already added. Only one instance allowed.");
                int keyIndex = _serviceKeys.Count;
                _serviceKeys.TryAdd(serviceKey, keyIndex);
                var instance = CreateMethodMap(keyIndex, serviceType, service);
                _services.TryAdd(keyIndex, instance);
            }
            catch (Exception e)
            {
                _log.Fatal("AddServive exception on {0}. Error: {1}", service.GetType(), e.ToString().Flatten());
                throw;
            }
        }

        /// <summary>
        /// Loads all methods from interfaces and assigns an identifier
        /// to each. These are later synchronized with the client.
        /// </summary>
        private ServiceInstance CreateMethodMap(int keyIndex, Type serviceType, object service)
        {
            var instance = new ServiceInstance
            {
                KeyIndex = keyIndex,
                InterfaceType = serviceType,
                InterfaceMethods = new ConcurrentDictionary<int, MethodInfo>(),
                MethodParametersByRef = new ConcurrentDictionary<int, bool[]>(),
                SingletonInstance = service
            };

            var currentMethodIdent = 0;
            if (serviceType.IsInterface)
            {
                var methodInfos = serviceType.GetMethods();
                foreach (var mi in methodInfos)
                {
                    instance.InterfaceMethods.TryAdd(currentMethodIdent, mi);
                    var parameterInfos = mi.GetParameters();
                    var isByRef = new bool[parameterInfos.Length];
                    for (int i = 0; i < isByRef.Length; i++)
                        isByRef[i] = parameterInfos[i].ParameterType.IsByRef;
                    instance.MethodParametersByRef.TryAdd(currentMethodIdent, isByRef);
                    currentMethodIdent++;
                }
            }

            var interfaces = serviceType.GetInterfaces();
            foreach (var interfaceType in interfaces)
            {
                var methodInfos = interfaceType.GetMethods();
                foreach (var mi in methodInfos)
                {
                    instance.InterfaceMethods.TryAdd(currentMethodIdent, mi);
                    var parameterInfos = mi.GetParameters();
                    var isByRef = new bool[parameterInfos.Length];
                    for (int i = 0; i < isByRef.Length; i++)
                        isByRef[i] = parameterInfos[i].ParameterType.IsByRef;
                    instance.MethodParametersByRef.TryAdd(currentMethodIdent, isByRef);
                    currentMethodIdent++;
                }
            }


            //Create a list of sync infos from the dictionary
            var syncSyncInfos = new List<MethodSyncInfo>();
            foreach (var kvp in instance.InterfaceMethods)
            {
                var parameters = kvp.Value.GetParameters();
                var parameterTypes = new string[parameters.Length];
                for (var i = 0; i < parameters.Length; i++)
                    parameterTypes[i] = parameters[i].ParameterType.FullName;
                syncSyncInfos.Add(new MethodSyncInfo
                {
                    MethodIdent = kvp.Key,
                    MethodName = kvp.Value.Name,
                    MethodReturnType = kvp.Value.ReturnType.FullName,
                    ParameterTypes = parameterTypes
                });
            }

            var serviceSyncInfo = new ServiceSyncInfo
            {
                ServiceKeyIndex = keyIndex,
                CompressionThreshold = _compressionThreshold,
                UseCompression = _useCompression,
                MethodInfos = syncSyncInfos.ToArray()
            };
            instance.ServiceSyncInfo = serviceSyncInfo;
            return instance;
        }

        /// <summary>
        /// Opens the host and starts a listener. This listener spawns a new thread (or uses a
        /// thread pool thread) for each incoming connection.
        /// </summary>
        public void Open()
        {
            _isOpen = true;
            StartListener();
        }

        protected abstract void StartListener();

        /// <summary>
        /// Closes the host and calls Dispose().
        /// </summary>
        public void Close()
        {
            Dispose();
        }

        protected void ProcessRequest(Stream stream)
        {
            if (null == stream || (!stream.CanWrite && !stream.CanRead))
            {
                _log.Error("Cannot process a request on a stream that is not read/write.");
                return;
            }
            ProcessRequest(stream, stream);
        }

        public bool IsPipeline;

        /// <summary>
        /// This method handles all requests from a single client.
        /// There is one thread running this method for each connected client.
        /// </summary>
        /// <param name="readStream">The read/write stream.</param>
        /// <param name="writeStream">The read/write stream.</param>
        protected virtual void ProcessRequest(Stream readStream, Stream writeStream)
        {
            if (null == readStream || null == writeStream) return;

            BinaryReader binReader = null;
            BinaryWriter binWriter = null;
            DuplexPipe duplexPipe = null;
			if (IsPipeline)
            {
                duplexPipe = new DuplexPipe((NetworkStream)readStream);
                duplexPipe.Start();
            }
            else
            {
                binReader = new BinaryReader(readStream);
                binWriter = new BinaryWriter(writeStream);
			}

            bool doContinue = true;
            try
            {
                ZkSession zkSession = null;
                do
                {
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        //read message type
                        var messageType = IsPipeline? (MessageType)duplexPipe.ReadInt() : (MessageType)binReader.ReadInt32();
                        switch (messageType)
                        {
                            case MessageType.ZkInitiate:
                                zkSession = new ZkSession(_zkRepository, _log, _stats);
                                doContinue = IsPipeline ? zkSession.ProcessZkInitiation(duplexPipe, sw) : zkSession.ProcessZkInitiation(binReader, binWriter, sw);
                                break;
                            case MessageType.ZkProof:
                                if (null == zkSession) throw new NullReferenceException("session null");
                                doContinue = IsPipeline ? zkSession.ProcessZkProof(duplexPipe, sw) : zkSession.ProcessZkProof(binReader, binWriter, sw);
								break;
                            case MessageType.SyncInterface:
                                if (IsPipeline)
                                    ProcessSync(zkSession, duplexPipe, sw);
                                else
                                    ProcessSync(zkSession, binReader, binWriter, sw);
                                break;
                            case MessageType.MethodInvocation:
                                if (IsPipeline)
									ProcessInvocation(zkSession, duplexPipe, sw);
								else
									ProcessInvocation(zkSession, binReader, binWriter, sw);

								break;
                            case MessageType.TerminateConnection:
                                doContinue = false;
                                _log.Info("Terminated");
								break;
                            default:
                                doContinue = false;
                                break;
                        }
                    }
                    catch (Exception e) //do not resume operation on this thread if any errors are unhandled.
                    {
                        _log.Error("Error in ProcessRequest: {0}", e.ToString().Flatten());
                        doContinue = false;
                    }
                    sw.Stop();
                }
                while (doContinue);
            }
            catch (Exception fatalException)
            {
                _log.Fatal("Fatal error in ProcessRequest: {0}", fatalException.ToString().Flatten());
            }
            finally
			{
				duplexPipe?.Stop();
				binReader?.Close();
                binWriter?.Close();
			}
        }

        private void ProcessSync(ZkSession session, BinaryReader binReader, BinaryWriter binWriter, Stopwatch sw)
        {
            var syncCat = "Sync";

            string serviceTypeName;
            if (_requireZk)
            {
                //use session and encryption - if throws should not have gotten this far
                var len = binReader.ReadInt32();
                var bytes = binReader.ReadBytes(len);
                var data = session.Crypto.Decrypt(bytes);
                serviceTypeName = data.ConvertToString();
            }
            else
            {
                serviceTypeName = binReader.ReadString();
            }

            int serviceKey;
            if (_serviceKeys.TryGetValue(serviceTypeName, out serviceKey))
            {
                ServiceInstance instance;
                if (_services.TryGetValue(serviceKey, out instance))
                {
                    syncCat = instance.InterfaceType.Name;
                    //Create a list of sync infos from the dictionary
                    var syncBytes = _serializer.Serialize(instance.ServiceSyncInfo);
                    if (_requireZk)
                    {
                        _log.Debug("Unencrypted data sent to server: {0}", Convert.ToBase64String(syncBytes));
                        var encData = session.Crypto.Encrypt(syncBytes);
                        binWriter.Write(encData.Length);
                        binWriter.Write(encData);
                        _log.Debug("Encrypted data sent server: {0}", Convert.ToBase64String(encData));
                    }
                    else
                    {
                        binWriter.Write(syncBytes.Length);
                        binWriter.Write(syncBytes);
                    }
                }
            }
            else
            {
                //return zero to indicate type or version of type not found
                binWriter.Write(0);
            }
            binWriter.Flush();
            _log.Debug("SyncInterface for {0} in {1}ms.", syncCat, sw.ElapsedMilliseconds);
        }

        private void ProcessSync(ZkSession session, DuplexPipe duplexPipe, Stopwatch sw)
        {
            var syncCat = "Sync";

            string serviceTypeName;
            if (_requireZk)
            {
                //use session and encryption - if throws should not have gotten this far
                var len = duplexPipe.ReadInt();
                var bytes = duplexPipe.Read(len);
                var data = session.Crypto.Decrypt(bytes);
                serviceTypeName = data.ConvertToString();
            }
            else
            {
                serviceTypeName = duplexPipe.ReadString();
            }

            int serviceKey;
            if (_serviceKeys.TryGetValue(serviceTypeName, out serviceKey))
            {
                ServiceInstance instance;
                if (_services.TryGetValue(serviceKey, out instance))
                {
                    syncCat = instance.InterfaceType.Name;
                    //Create a list of sync infos from the dictionary
                    var syncBytes = _serializer.Serialize(instance.ServiceSyncInfo);
                    if (_requireZk)
                    {
                        _log.Debug("Unencrypted data sent to server: {0}", Convert.ToBase64String(syncBytes));
                        var encData = session.Crypto.Encrypt(syncBytes);
                        duplexPipe.Write(encData.Length);
                        duplexPipe.Write((ReadOnlySpan<byte>) encData);
                        _log.Debug("Encrypted data sent server: {0}", Convert.ToBase64String(encData));
                    }
                    else
                    {
                        duplexPipe.Write(syncBytes.Length);
                        duplexPipe.Write((ReadOnlySpan<byte>) syncBytes);
                    }
                }
            }
            else
            {
                //return zero to indicate type or version of type not found
                duplexPipe.Write(0);
            }

            _log.Debug("SyncInterface for {0} in {1}ms.", syncCat, sw.ElapsedMilliseconds);
        }

        private void ProcessInvocation(ZkSession session, BinaryReader binReader, BinaryWriter binWriter, Stopwatch sw)
        {
            //read service instance key
            var cat = "unknown";
            var stat = "MethodInvocation";
            int invokedServiceKey = binReader.ReadInt32();
            ServiceInstance invokedInstance;
            if (_services.TryGetValue(invokedServiceKey, out invokedInstance))
            {
                cat = invokedInstance.InterfaceType.Name;
                //read the method identifier
                int methodHashCode = binReader.ReadInt32();
                if (invokedInstance.InterfaceMethods.ContainsKey(methodHashCode))
                {
                    MethodInfo method;
                    invokedInstance.InterfaceMethods.TryGetValue(methodHashCode, out method);
                    stat = method.Name;

                    bool[] isByRef;
                    invokedInstance.MethodParametersByRef.TryGetValue(methodHashCode, out isByRef);

                    //read parameter data
                    object[] parameters;
                    if (_requireZk)
                    {
                        var len = binReader.ReadInt32();
                        var encData = binReader.ReadBytes(len);
                        _log.Debug("Encrypted data received from server: {0}", Convert.ToBase64String(encData));
                        var data = session.Crypto.Decrypt(encData);
                        _log.Debug("Decrypted data received from server: {0}", Convert.ToBase64String(data));
                        using (var ms = new MemoryStream(data))
                        using (var br = new BinaryReader(ms))
                        {
                            parameters = _parameterTransferHelper.ReceiveParameters(br);
                        }
                    }
                    else
                    {
                        parameters = _parameterTransferHelper.ReceiveParameters(binReader);
                    }

                    //invoke the method
                    object[] returnParameters;
                    var returnMessageType = MessageType.ReturnValues;
                    try
                    {
                        object returnValue = method.Invoke(invokedInstance.SingletonInstance, parameters);
                        if (returnValue is Task task)
                        {
	                        task.GetAwaiter().GetResult();
	                        var prop = task.GetType().GetProperty("Result");
	                        returnValue = prop?.GetValue(task);
						}
                        //the result to the client is the return value (null if void) and the input parameters
                        returnParameters = new object[1 + parameters.Length];
                        returnParameters[0] = returnValue;
                        for (int i = 0; i < parameters.Length; i++)
                            returnParameters[i + 1] = isByRef[i] ? parameters[i] : null;
                    }
                    catch (Exception ex)
                    {
                        //an exception was caught. Rethrow it client side
                        returnParameters = new object[] {ex};
                        returnMessageType = MessageType.ThrowException;
                    }

                    //send the result back to the client
                    // (1) write the message type
                    binWriter.Write((int) returnMessageType);

                    // (2) write the return parameters
                    if (_requireZk)
                    {
                        byte[] data;
                        using (var ms = new MemoryStream())
                        using (var bw = new BinaryWriter(ms))
                        {
                            _parameterTransferHelper.SendParameters(
                                invokedInstance.ServiceSyncInfo.UseCompression,
                                invokedInstance.ServiceSyncInfo.CompressionThreshold,
                                bw,
                                returnParameters);
                            data = ms.ToArray();
                        }
                        _log.Debug("Unencrypted data sent server: {0}", Convert.ToBase64String(data));
                        var encData = session.Crypto.Encrypt(data);
                        _log.Debug("Encrypted data sent server: {0}", Convert.ToBase64String(encData));
                        binWriter.Write(encData.Length);
                        binWriter.Write(encData);
                    }
                    else
                    {
                        _parameterTransferHelper.SendParameters(
                            invokedInstance.ServiceSyncInfo.UseCompression,
                            invokedInstance.ServiceSyncInfo.CompressionThreshold,
                            binWriter,
                            returnParameters);
                    }
                }
                else
                    binWriter.Write((int) MessageType.UnknownMethod);
            }
            else
                binWriter.Write((int) MessageType.UnknownMethod);

            //flush
            binWriter.Flush();
            _stats.Log(cat, stat, sw.ElapsedMilliseconds);
        }

        private void ProcessInvocation(ZkSession session, IDuplexPipe duplexPipe, Stopwatch sw)
		{
			//read service instance key
			var cat = "unknown";
			var stat = "MethodInvocation";
			int invokedServiceKey = duplexPipe.ReadInt();
			ServiceInstance invokedInstance;
			if (_services.TryGetValue(invokedServiceKey, out invokedInstance))
			{
				cat = invokedInstance.InterfaceType.Name;
				//read the method identifier
				int methodHashCode = duplexPipe.ReadInt();
				if (invokedInstance.InterfaceMethods.ContainsKey(methodHashCode))
				{
					MethodInfo method;
					invokedInstance.InterfaceMethods.TryGetValue(methodHashCode, out method);
					stat = method.Name;

					bool[] isByRef;
					invokedInstance.MethodParametersByRef.TryGetValue(methodHashCode, out isByRef);

					//read parameter data
                    if (_requireZk)
					{
						var len = duplexPipe.ReadInt();
						var encData = duplexPipe.Read(len);

                        var encrypted = session.Crypto.Decrypt(encData);
                        _ = duplexPipe.AppendToBuffer(encrypted);
					}

                    var parameters = _parameterTransferHelper.ReceiveParameters(duplexPipe);

                    //invoke the method
                    object[] returnParameters;
					var returnMessageType = MessageType.ReturnValues;
					try
					{
						object returnValue = method.Invoke(invokedInstance.SingletonInstance, parameters);
						if (returnValue is Task task)
						{
							task.GetAwaiter().GetResult();
							var prop = task.GetType().GetProperty("Result");
							returnValue = prop?.GetValue(task);
						}
						//the result to the client is the return value (null if void) and the input parameters
						returnParameters = new object[1 + parameters.Length];
						returnParameters[0] = returnValue;
						for (int i = 0; i < parameters.Length; i++)
							returnParameters[i + 1] = isByRef[i] ? parameters[i] : null;
					}
					catch (Exception ex)
					{
						//an exception was caught. Rethrow it client side
						returnParameters = new object[] { ex };
						returnMessageType = MessageType.ThrowException;
					}

					//send the result back to the client
					// (1) write the message type
					duplexPipe.Write((int)returnMessageType);

					// (2) write the return parameters
					if (_requireZk)
					{
                        using (IDuplexPipe simplePipe = new FakeDuplexPipe())
                        {
                            _parameterTransferHelper.SendParameters(
                                invokedInstance.ServiceSyncInfo.UseCompression,
                                invokedInstance.ServiceSyncInfo.CompressionThreshold,
                                simplePipe,
                                returnParameters);
                            byte[] data = simplePipe.ReadAsync().ToArray();
                            _log.Debug("Unencrypted data sent server: {0}", Convert.ToBase64String(data));
                            var encData = session.Crypto.Encrypt(data);
                            _log.Debug("Encrypted data sent server: {0}", Convert.ToBase64String(encData));
                            duplexPipe.Write(encData.Length);
                            duplexPipe.Write((ReadOnlySpan<byte>) encData);
                        }

					}
					else
					{
						_parameterTransferHelper.SendParameters(
							invokedInstance.ServiceSyncInfo.UseCompression,
							invokedInstance.ServiceSyncInfo.CompressionThreshold,
							duplexPipe,
							returnParameters);
					}
				}
				else
					duplexPipe.Write((int)MessageType.UnknownMethod);
			}
			else
				duplexPipe.Write((int)MessageType.UnknownMethod);

			_stats.Log(cat, stat, sw.ElapsedMilliseconds);
		}

		#region IDisposable Members

		private bool _disposed = false;

        public void Dispose()
        {
            //MS recommended dispose pattern - prevents GC from disposing again
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
	        if (_disposed) return;
	        _disposed = true; //prevent second call to Dispose
            if (disposing)
            {
	            if (_log is Logger log) log.FlushLog();
	            if (_stats is Stats stat) stat.FlushLog();
	            _isOpen = false;
	            Continue = false;
	            foreach (var instance in _services)
	            {
		            if (instance.Value.SingletonInstance is IDisposable disposable) disposable.Dispose();
	            }
            }
        }

        #endregion
    }
}
