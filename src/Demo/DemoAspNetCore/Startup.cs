using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DemoCommon;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using ServiceWire;

namespace DemoAspNetCore
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddCors(options =>
            {
                options.AddPolicy("CorsPolicy",
                    builder => builder.AllowAnyOrigin()
                        .AllowAnyMethod()
                        .AllowAnyHeader()
                        .AllowCredentials());
            });
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            var informationResolver = new ServiceInformationResolver(new InstanceActivator());
            informationResolver.AddService<IDataContract, DataContract>();

            app.UseCors("CorsPolicy");

            app.UseServiceWireRpc(configuration =>
            {
                configuration.Resolver = informationResolver;
                configuration.Serializer = new NewtonsoftSerializer();
            });
        }
    }

    //should be moved to their own project
    public static class ServiceWireExtension
    {
        public class ServiceWireRpc
        {
            private readonly RequestDelegate _next;
            private readonly RpcConfiguration _configuration;

            public ServiceWireRpc(RequestDelegate next, RpcConfiguration configuration)
            {
                this._next = next;
                this._configuration = configuration;
            }

            public async Task InvokeAsync(HttpContext context)
            {
                if (context.Request.Method == "POST" && context.Request.Path.Equals("/rpc"))
                {
                    var host = new HttpHost(_configuration.Resolver, _configuration.Serializer);
                    await host.ProcessRequest(context.Request.Body, context.Response.Body);
                }
                
                await _next(context);
            }
        }

        public static IApplicationBuilder UseServiceWireRpc(this IApplicationBuilder builder, Action<RpcConfiguration> configuration)
        {
            var rpcConfiguration = new RpcConfiguration();
            configuration?.Invoke(rpcConfiguration);

            return builder.UseMiddleware<ServiceWireRpc>(rpcConfiguration);
        }

        public class RpcConfiguration
        {
            public ISerializer Serializer { get; set; } = new DefaultSerializer();

            public ServiceInformationResolver Resolver { get; set; }
        }
    }

    //test contract implementation
    public class DataContract : IDataContract
    {
        public decimal GetDecimal(decimal input)
        {
            return 132;
        }

        public bool OutDecimal(decimal val)
        {
            return true;
        }
    }

    //test serializer implementation
    public class NewtonsoftSerializer : ISerializer
    {
        private JsonSerializerSettings settings = new JsonSerializerSettings
        {
            ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore
        };

        public T Deserialize<T>(byte[] bytes)
        {
            if (null == bytes || bytes.Length == 0) return default(T);
            var json = Encoding.UTF8.GetString(bytes);
            return JsonConvert.DeserializeObject<T>(json, settings);
        }

        public object Deserialize(byte[] bytes, string typeConfigName)
        {
            if (null == typeConfigName) throw new ArgumentNullException(nameof(typeConfigName));
            var type = typeConfigName.ToType();
            if (null == typeConfigName || null == bytes || bytes.Length == 0) return type.GetDefault();
            var json = Encoding.UTF8.GetString(bytes);
            return JsonConvert.DeserializeObject(json, type, settings);
        }

        public byte[] Serialize<T>(T obj)
        {
            if (null == obj) return null;
            var json = JsonConvert.SerializeObject(obj, settings);
            return Encoding.UTF8.GetBytes(json);
        }

        public byte[] Serialize(object obj, string typeConfigName)
        {
            if (null == obj) return null;
            var type = typeConfigName.ToType();
            var json = JsonConvert.SerializeObject(obj, type, settings);
            return Encoding.UTF8.GetBytes(json);
        }
    }
}
