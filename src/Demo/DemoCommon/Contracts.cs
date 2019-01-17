﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DemoCommon
{
	public interface ITest
	{
		Task SetAsync(int a);
		Task<string[]> GetAsync();
        string[] GetItems(Guid id);
    }

    public interface IDataContract
    {
        decimal GetDecimal(decimal input);
        bool OutDecimal(decimal val);
    }

    public interface IComplexDataContract
    {
        Guid GetId(string source, double weight, int quantity, DateTime dt);
        ComplexResponse Get(Guid id, string label, double weight, out long quantity);
        long TestLong(out long id1, out long id2);
        List<string> GetItems(Guid id);
    }

    public struct ComplexResponse
    {
        public Guid Id { get; set; }
        public string Label { get; set; }
        public long Quantity { get; set; }
    }
}
