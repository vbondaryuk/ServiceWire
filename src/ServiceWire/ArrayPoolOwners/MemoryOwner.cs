using System;
using System.Buffers;

namespace ServiceWire.ArrayPoolOwners
{
	public static class MemoryOwner
	{
		public static ICustomMemoryOwner<T> Lease<T>(
			this ReadOnlySequence<T> source)
		{
			if (source.IsEmpty) return EmptyMemoryOwner<T>.Empty;

			int len = checked((int)source.Length);
			var arr = ArrayPool<T>.Shared.Rent(len);
			source.CopyTo(arr);
			return new ArrayPoolOwner<T>(arr, len);
		}

		public static ICustomMemoryOwner<T> Lease<T>(
			this ReadOnlyMemory<T> source)
		{
			if (source.IsEmpty) return EmptyMemoryOwner<T>.Empty;

			T[] array = ArrayPool<T>.Shared.Rent(source.Length);
			source.CopyTo(array);
			return new ArrayPoolOwner<T>(array, source.Length);

		}

		public static ICustomMemoryOwner<T> Lease<T>(this T[] source, int length = -1)
		{
			if (source == null) return null;
			if (length < 0) length = source.Length;
			else if (length > source.Length) throw new ArgumentOutOfRangeException(nameof(length));
			return length == 0 ? EmptyMemoryOwner<T>.Empty : new ArrayPoolOwner<T>(source, length);
		}
	}
}