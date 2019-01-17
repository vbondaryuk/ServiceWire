using System;
using System.Buffers;
using System.Threading;

namespace ServiceWire.ArrayPoolOwners
{
	public sealed class ArrayPoolOwner<T> : ICustomMemoryOwner<T>
	{
		private int _length;
		private T[] _oversized;

		internal ArrayPoolOwner(T[] oversized, int length)
		{
			_length = length;
            _oversized = oversized;
		}

		public static ICustomMemoryOwner<T> Rent(int length)
		{
			T[] bytes = ArrayPool<T>.Shared.Rent(length);
			return new ArrayPoolOwner<T>(bytes, length);
		}

		public Memory<T> Memory => new Memory<T>(GetArray(), 0, _length);


        public T[] Array => GetArray();


        public T[] GetArray() =>
			Interlocked.CompareExchange(ref _oversized, null, null)
			?? throw new ObjectDisposedException(ToString());

		public void Dispose()
		{
			GC.SuppressFinalize(this);
			var arr = Interlocked.Exchange(ref _oversized, null);
			if (arr != null) ArrayPool<T>.Shared.Return(arr);
		}
	}
}