using System;

namespace ServiceWire.ArrayPoolOwners
{
	public sealed class EmptyMemoryOwner<T> : ICustomMemoryOwner<T>
	{
		public static ICustomMemoryOwner<T> Empty { get; } = new EmptyMemoryOwner<T>(Memory<T>.Empty);

		public EmptyMemoryOwner(Memory<T> memory) => Memory = memory;

		public Memory<T> Memory { get; }

        public T[] Array => System.Array.Empty<T>();
        public void Dispose() { }
	}
}