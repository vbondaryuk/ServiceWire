using System.Buffers;

namespace ServiceWire.ArrayPoolOwners
{
	public interface ICustomMemoryOwner<T> : IMemoryOwner<T>
	{
		T[] Array { get; }
	}
}