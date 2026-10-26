using System.Text;

internal static class Program
{
    private const ulong Half = 1UL << 63;
    private const ulong Quarter = 1UL << 62;
    private const ulong ThreeQuarters = 3UL << 62;
    private static readonly byte[] Magic = "ARC1"u8.ToArray();
    private static readonly UTF8Encoding Utf8 = new(false, true);

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Utf8;

        if (args.Length == 1 && args[0] is "--help" or "-h")
        {
            PrintUsage();
            return 0;
        }

        if (args.Length != 2)
        {
            PrintUsage();
            return 1;
        }

        try
        {
            if (args[0] == "--text")
            {
                Console.WriteLine(Convert.ToBase64String(Compress(Utf8.GetBytes(args[1]))));
                return 0;
            }

            byte[] archive = Compress(File.ReadAllBytes(args[0]));

            using (FileStream output = new(args[1], FileMode.CreateNew, FileAccess.Write))
            {
                output.Write(archive);
            }

            Console.WriteLine($"Saved: {args[1]}");
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                          or ArgumentException or NotSupportedException or OverflowException)
        {
            Console.Error.WriteLine(exception switch
            {
                FileNotFoundException => "Error: file not found.",
                DirectoryNotFoundException => "Error: directory not found.",
                UnauthorizedAccessException => "Error: access denied.",
                IOException => "Error: file I/O failed.",
                OverflowException => "Error: size limit exceeded.",
                _ => "Error: invalid argument."
            });
            return 1;
        }
    }

    private static byte[] Compress(byte[] input)
    {
        long[] frequencies = new long[256];

        foreach (byte symbol in input)
        {
            frequencies[symbol]++;
        }

        long[] cumulative = BuildCumulative(frequencies);
        long total = cumulative[^1];
        ushort symbolCount = 0;

        foreach (long frequency in frequencies)
        {
            if (frequency > 0)
            {
                symbolCount++;
            }
        }

        BitWriter bits = new();
        ulong low = 0;
        ulong high = ulong.MaxValue;
        long pendingBits = 0;

        foreach (byte symbol in input)
        {
            EncodeSymbol(symbol, cumulative, total, bits, ref low, ref high, ref pendingBits);
        }

        EncodeSymbol(256, cumulative, total, bits, ref low, ref high, ref pendingBits);
        pendingBits++;
        WriteResolvedBit(bits, ref pendingBits, low < Quarter ? 0 : 1);

        for (int index = 0; index < 64; index++)
        {
            bits.WriteBit(0);
        }

        byte[] payload = bits.ToArray();
        long archiveSize = 22L + symbolCount * 9L + payload.Length;

        if (archiveSize > Array.MaxLength)
        {
            throw new OverflowException();
        }

        using MemoryStream stream = new((int)archiveSize);
        using BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write((long)input.Length);
        writer.Write(bits.BitCount);
        writer.Write(symbolCount);

        for (int symbol = 0; symbol < frequencies.Length; symbol++)
        {
            if (frequencies[symbol] > 0)
            {
                writer.Write((byte)symbol);
                writer.Write(frequencies[symbol]);
            }
        }

        writer.Write(payload);
        writer.Flush();
        return stream.ToArray();
    }

    private static long[] BuildCumulative(long[] frequencies)
    {
        long[] cumulative = new long[258];

        for (int symbol = 0; symbol < frequencies.Length; symbol++)
        {
            cumulative[symbol + 1] = cumulative[symbol] + frequencies[symbol];
        }

        cumulative[^1] = cumulative[^2] + 1;
        return cumulative;
    }

    private static void EncodeSymbol(int symbol, long[] cumulative, long total, BitWriter bits,
                                     ref ulong low, ref ulong high, ref long pendingBits)
    {
        UInt128 range = (UInt128)high - low + 1;
        UInt128 start = low;
        high = (ulong)(start + range * (ulong)cumulative[symbol + 1] / (ulong)total - 1);
        low = (ulong)(start + range * (ulong)cumulative[symbol] / (ulong)total);

        while (true)
        {
            if (high < Half)
            {
                WriteResolvedBit(bits, ref pendingBits, 0);
            }
            else if (low >= Half)
            {
                WriteResolvedBit(bits, ref pendingBits, 1);
                low -= Half;
                high -= Half;
            }
            else if (low >= Quarter && high < ThreeQuarters)
            {
                pendingBits++;
                low -= Quarter;
                high -= Quarter;
            }
            else
            {
                break;
            }

            low <<= 1;
            high = (high << 1) | 1;
        }
    }

    private static void WriteResolvedBit(BitWriter bits, ref long pendingBits, int bit)
    {
        bits.WriteBit(bit);

        while (pendingBits > 0)
        {
            bits.WriteBit(1 - bit);
            pendingBits--;
        }
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage: ArithmeticCompress <input> <output.arc>");
        Console.Error.WriteLine("   or: ArithmeticCompress --text <text>");
    }

    private sealed class BitWriter
    {
        private readonly List<byte> _bytes = new();
        private byte _current;
        private int _occupied;

        public long BitCount { get; private set; }

        public void WriteBit(int bit)
        {
            if (bit == 1)
            {
                _current |= (byte)(1 << (7 - _occupied));
            }

            _occupied++;
            BitCount++;

            if (_occupied == 8)
            {
                _bytes.Add(_current);
                _current = 0;
                _occupied = 0;
            }
        }

        public byte[] ToArray()
        {
            if (_occupied > 0)
            {
                _bytes.Add(_current);
                _current = 0;
                _occupied = 0;
            }

            return _bytes.ToArray();
        }
    }
}


// dotnet run --project .\ArithmeticCoding\Compress\ArithmeticCompress.csproj -c Release -- input.txt .\ArithmeticCoding\compressed.arc