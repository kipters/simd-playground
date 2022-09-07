using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

#if DEBUG

Console.WriteLine($"Vectors\n   64: {Vector64.IsHardwareAccelerated}\n  128: {Vector128.IsHardwareAccelerated}\n  256: {Vector256.IsHardwareAccelerated}");

var supportedIntrinsics = RuntimeInformation.ProcessArchitecture switch
{
    Architecture.Arm => ArmSupport(),
    Architecture.Arm64 => ArmSupport(),
    Architecture.Armv6 => ArmSupport(),
    Architecture.X86 => IntelSupport(),
    Architecture.X64 => IntelSupport(),
    _ => "Unknown, unsupported or cursed CPU architecture"
};

Console.WriteLine($"Supported intrinsics ({RuntimeInformation.ProcessArchitecture}):\n{supportedIntrinsics}\n");

string IsaSupport(string @namespace)
{
    var isas = AppDomain.CurrentDomain
        .GetAssemblies()
        .SelectMany(_ => _.ExportedTypes)
        .Where(x => x.Namespace == @namespace)
        .Where(x => !x.FullName.Contains('+'))
        .Select(x => (type: x, prop: x.GetProperty("IsSupported", typeof(bool))))
        .Where(t => t.prop is not null)
        .Select(x => $"  {x.type.Name}: {ToEmoji(x.prop!.GetValue(null))}");

    return string.Join('\n', isas);

    string ToEmoji(object? obj) => obj switch
    {
        bool b when b == true => "✔",
        bool b when b == false => "❌",
        null => "🚫",
        _ => "⁉"
    };
}

string ArmSupport() => IsaSupport("System.Runtime.Intrinsics.Arm");
string IntelSupport() => IsaSupport("System.Runtime.Intrinsics.X86");

var benchmarks = new Benchmarks { N = 16 };
benchmarks.GlobalSetup();

var results = typeof(Benchmarks)
    .GetMethods()
    .Where(m => m.GetCustomAttribute<BenchmarkAttribute>() is not null)
    .Select(m => (
        name: m.Name,
        result: m.Invoke(benchmarks, null) as double?
    ));

foreach (var result in results)
{
    Console.WriteLine($"{result.result}\t{result.name}");
}
#else
BenchmarkRunner.Run<Benchmarks>();
#endif

public class Benchmarks
{
    [Params(16, 1 << 26)]
    public int N;

    public float[] A = Array.Empty<float>();
    public float[] B = Array.Empty<float>();

    [GlobalSetup]
    public void GlobalSetup()
    {
        A = Enumerable.Range(0, N)
                .Select(_ => 2f * Random.Shared.NextSingle() - 1f)
                .ToArray();

        B = Enumerable.Range(0, N)
                .Select(_ => 2f * Random.Shared.NextSingle() - 1f)
                .ToArray();
    }

    [Benchmark(Baseline = true)]
    public double Scalar()
    {
        double acc = 0;

        for (int i = 0; i < N; i++)
        {
            acc += (double)A[i] * (double)B[i];
        }

        return acc;
    }

    [Benchmark]
    public double Linq() => A
        .Zip(B)
        .Select(t => (double) t.First * (double) t.Second)
        .Sum();

    [Benchmark]
    public double Intrinsic()
    {
        double acc = 0;

        ref float ptrA = ref MemoryMarshal.GetReference<float>(A);
        ref float ptrB = ref MemoryMarshal.GetReference<float>(B);

        ref float endMinusOne = ref Unsafe.Add(ref ptrA, A.Length - Vector256<float>.Count);

        Vector256<float> a, b;

        do
        {
            a = Vector256.LoadUnsafe(ref ptrA);
            b = Vector256.LoadUnsafe(ref ptrB);

            acc += Vector256.Dot(a, b);

            ptrA = ref Unsafe.Add(ref ptrA, Vector256<float>.Count);
            ptrB = ref Unsafe.Add(ref ptrB, Vector256<float>.Count);
        }
        while (Unsafe.IsAddressLessThan(ref ptrA, ref endMinusOne));

        a = Vector256.LoadUnsafe(ref ptrA);
        b = Vector256.LoadUnsafe(ref ptrB);

        acc += Vector256.Dot(a, b);

        return acc;
    }

    [Benchmark]
    public unsafe double Vectorized()
    {
        var acc = Vector256<float>.Zero;

        ref float ptrA = ref MemoryMarshal.GetReference<float>(A);
        ref float ptrB = ref MemoryMarshal.GetReference<float>(B);

        ref float endMinusOne = ref Unsafe.Add(ref ptrA, A.Length - Vector256<float>.Count);

        Vector256<float> a, b;

        do
        {
            a = Vector256.LoadUnsafe(ref ptrA);
            b = Vector256.LoadUnsafe(ref ptrB);

            acc += a * b;

            ptrA = ref Unsafe.Add(ref ptrA, Vector256<float>.Count);
            ptrB = ref Unsafe.Add(ref ptrB, Vector256<float>.Count);
        }
        while (Unsafe.IsAddressLessThan(ref ptrA, ref endMinusOne));

        a = Vector256.LoadUnsafe(ref ptrA);
        b = Vector256.LoadUnsafe(ref ptrB);

        acc += a * b;

        return Vector256.Sum(acc);
    }

    [Benchmark]
    public unsafe double SmallVectorized()
    {
        var acc = Vector128<float>.Zero;

        ref float ptrA = ref MemoryMarshal.GetReference<float>(A);
        ref float ptrB = ref MemoryMarshal.GetReference<float>(B);

        ref float endMinusOne = ref Unsafe.Add(ref ptrA, A.Length - Vector128<float>.Count);

        Vector128<float> a, b;

        do
        {
            a = Vector128.LoadUnsafe(ref ptrA);
            b = Vector128.LoadUnsafe(ref ptrB);

            acc += a * b;

            ptrA = ref Unsafe.Add(ref ptrA, Vector128<float>.Count);
            ptrB = ref Unsafe.Add(ref ptrB, Vector128<float>.Count);
        }
        while (Unsafe.IsAddressLessThan(ref ptrA, ref endMinusOne));

        a = Vector128.LoadUnsafe(ref ptrA);
        b = Vector128.LoadUnsafe(ref ptrB);

        acc += a * b;

        return Vector128.Sum(acc);
    }

    [Benchmark]
    public unsafe double SmallestVectorized()
    {
        var acc = Vector64<float>.Zero;

        ref float ptrA = ref MemoryMarshal.GetReference<float>(A);
        ref float ptrB = ref MemoryMarshal.GetReference<float>(B);

        ref float endMinusOne = ref Unsafe.Add(ref ptrA, A.Length - Vector64<float>.Count);

        Vector64<float> a, b;

        do
        {
            a = Vector64.LoadUnsafe(ref ptrA);
            b = Vector64.LoadUnsafe(ref ptrB);

            acc += a * b;

            ptrA = ref Unsafe.Add(ref ptrA, Vector64<float>.Count);
            ptrB = ref Unsafe.Add(ref ptrB, Vector64<float>.Count);
        }
        while (Unsafe.IsAddressLessThan(ref ptrA, ref endMinusOne));

        a = Vector64.LoadUnsafe(ref ptrA);
        b = Vector64.LoadUnsafe(ref ptrB);

        acc += a * b;

        return Vector64.Sum(acc);
    }
}