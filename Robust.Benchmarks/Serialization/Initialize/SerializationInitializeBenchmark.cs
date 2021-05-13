﻿using BenchmarkDotNet.Attributes;
using Robust.Shared.Serialization.Manager;

namespace Robust.Benchmarks.Serialization.Initialize
{
    public class SerializationInitializeBenchmark : SerializationBenchmark
    {
        [IterationCleanup]
        public void IterationCleanup()
        {
            SerializationManager.Shutdown();
        }

        [Benchmark]
        public ISerializationManager Initialize()
        {
            InitializeSerialization();
            return SerializationManager;
        }
    }
}
