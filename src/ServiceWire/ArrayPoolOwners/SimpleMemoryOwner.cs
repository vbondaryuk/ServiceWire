using System;

namespace ServiceWire.ArrayPoolOwners
{
	public sealed class SimpleMemoryOwner<T> : ICustomMemoryOwner<T>
	{
		public static ICustomMemoryOwner<T> Empty { get; } = new SimpleMemoryOwner<T>(Memory<T>.Empty);

		public SimpleMemoryOwner(Memory<T> memory) => Memory = memory;

		public Memory<T> Memory { get; }

		public T[] Array => System.Array.Empty<T>();

		public void Dispose() { }
	}
}