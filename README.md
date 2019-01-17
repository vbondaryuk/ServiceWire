ServiceWire
===========

### BREAKING CHANGES in version 5.3.0

1. Injectable serialization (see project library tests for examples). 

2. Removes dependency on Newtonsoft.Json and uses BinaryFormatter for default serialization which means wire data classes must be marked [Serializable]. 

3. Internal classes are attributed to support protobuf-net serialization as well.

### Changes in version 5.2.0

1. Adds support for return types of Task and Task<T> to support async / await across the wire.

### Changes including some breaking changes in version 5.1.0

1. Dropped strong named assembly.

2. Support for NetCoreApp 2.0, 2.2 and .NET Framework 4.62. Dropped support for .NET 3.5.

3. Modified projects and NuGet package generation from Visual Studio 2017.

4. Dropped separate projects used to build different targets.

5. Converted test projects to XUnit with multiple targets to allow "dotnet test" run of all targets.


### Breaking Changes in version 4.0.1

1. Switched ServiceWire (and ServiceMq) to Newtonsoft.Json for serialization. Eliminates use of BinaryFormatter and its required Serializable attribute. Also eliminates ServiceStack.Text 3 dependency which has problems serializing structs.

2. Relaxed assembly version matching to allow additive changes without breaking the client or requiring an immediate client update.

3. Strong name added to allow the library to be used by strong named applications and libraries.

4. Added .NET 3.5 support to allow legacy applications to use the library. This adds a Framework specific dependency on TaskParallelLibrary 1.0.2856.0.

5. For the .NET 4.0 and 3.5 versions, changed to "Client Profile" for the target framework.

6. Removed dependency on System.Numerics in order to support .NET 3.5 and introduced ZkBigInt class taken from Scott Garland's BigInteger class. See license text for full attribution.

### A Lightweight Services Library for .NET.

ServiceWire is a very fast and light weight services host and dynamic client library that simplifies the development and use of high performance remote procedure call (RPC) communication between .NET processes over Named Pipes or TCP/IP.

Find "how to use" examples in the tests code. [ServiceWire documentation][] is available on the wiki.

### Important

ServiceWire's dynamically generated proxy will NOT run as x86 on an x64 system. This ususally occurs when you use Visual Studio to create a console application with the default "prefer x86" in project properties selected. Just be sure to choose AnyCPU or the specific target (x86 or x64) so that you do not run 32bit in WOW on an x64 machine.

### Get It on Nuget

Get the [NuGet package here][].

### Using the library is easy. 

1.  Code your interface

2.  Code your implementation

3.  Host the implementation

4.  Use dynamic proxy of your interface on the client side

### This unique library supports:

-   TCP and NamedPipes protocols

-   ByRef (out and ref) parameters (except for non-primitive value types)

-   Dynamic client proxy generation from service interface

-   Very fast serialization of most native types and arrays of those types

-   Multiple service interface hosting on the same endpoint

-   Aspect oriented interception with pre-, post- and exception handling cross cutting

-   Hosting of single service implementation singleton on multiple endpoints and protocols

-   Protocol, serialization and execution strategy extension

Portions of this library are a derivative of [RemotingLite][].  

  [NuGet package here]: http://www.nuget.org/packages/ServiceWire/
  [RemotingLite]: http://remotinglite.codeplex.com/
  [ServiceWire documentation]: https://github.com/tylerjensen/ServiceWire/wiki
