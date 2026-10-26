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
                Console.Write(Utf8.GetString(Decompress(Convert.FromBase64String(args[1]))));
                return 0;
            }

            byte[] restored = Decompress(File.ReadAllBytes(args[0]));

            using (FileStream output = new(args[1], FileMode.CreateNew, FileAccess.Write))
            {
                output.Write(restored);
            }

            Console.WriteLine($"Saved: {args[1]}");
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                          or ArgumentException or NotSupportedException
                                          or InvalidDataException or FormatException or OverflowException)
        {
            Console.Error.WriteLine(exception switch
            {
                FileNotFoundException => "Error: file not found.",
                DirectoryNotFoundException => "Error: directory not found.",
                UnauthorizedAccessException => "Error: access denied.",
                InvalidDataException or EndOfStreamException => "Error: invalid archive.",
                DecoderFallbackException => "Error: invalid UTF-8.",
                FormatException => "Error: invalid Base64.",
                IOException => "Error: file I/O failed.",
                OverflowException => "Error: size limit exceeded.",
                _ => "Error: invalid argument."
            });
            return 1;
        }
    }

    private static byte[] Decompress(byte[] archive)
    {
        using MemoryStream stream = new(archive, writable: false);
        using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: true);

        if (!reader.ReadBytes(Magic.Length).AsSpan().SequenceEqual(Magic))
        {
            throw new InvalidDataException("Invalid ARC1 archive.");
        }

        long originalLength = reader.ReadInt64();
        long bitCount = reader.ReadInt64();
        int symbolCount = reader.ReadUInt16();

        if (originalLength < 0 || originalLength > Array.MaxLength || bitCount < 66 || symbolCount > 256)
        {
            throw new InvalidDataException("Invalid archive header or unsupported size.");
        }

        long[] frequencies = new long[256];
        long frequencySum = 0;
        int previousSymbol = -1;

        for (int index = 0; index < symbolCount; index++)
        {
            int symbol = reader.ReadByte();
            long frequency = reader.ReadInt64();

            if (symbol <= previousSymbol || frequency <= 0 || frequency > originalLength - frequencySum)
            {
                throw new InvalidDataException("Invalid frequency table.");
            }

            frequencies[symbol] = frequency;
            frequencySum += frequency;
            previousSymbol = symbol;
        }

        if (frequencySum != originalLength)
        {
            throw new InvalidDataException("Frequency sum does not match the original size.");
        }

        long payloadSize = bitCount / 8 + (bitCount % 8 == 0 ? 0 : 1);

        if (stream.Length - stream.Position != payloadSize)
        {
            throw new InvalidDataException("Invalid payload length.");
        }

        int paddingBits = (int)((8 - bitCount % 8) % 8);

        if ((archive[^1] & ((1 << paddingBits) - 1)) != 0)
        {
            throw new InvalidDataException("Invalid padding bits.");
        }

        BitReader bits = new(archive, (int)stream.Position, bitCount);

        for (long index = bitCount - 64; index < bitCount; index++)
        {
            if (bits.BitAt(index) != 0)
            {
                throw new InvalidDataException("Invalid arithmetic padding.");
            }
        }

        long[] cumulative = BuildCumulative(frequencies);
        long total = cumulative[^1];
        long[] remaining = (long[])frequencies.Clone();
        byte[] result = new byte[(int)originalLength];
        ulong low = 0;
        ulong high = ulong.MaxValue;
        ulong code = 0;
        int outputPosition = 0;

        for (int index = 0; index < 64; index++)
        {
            code = (code << 1) | (uint)bits.ReadBit();
        }

        while (true)
        {
            if (code < low || code > high)
            {
                throw new InvalidDataException("Invalid arithmetic code.");
            }

            UInt128 range = (UInt128)high - low + 1;
            UInt128 scaled = (((UInt128)code - low + 1) * (ulong)total - 1) / range;

            if (scaled >= (ulong)total)
            {
                throw new InvalidDataException("Invalid arithmetic code.");
            }

            int symbol = FindSymbol(cumulative, (ulong)scaled);

            if (symbol == 256)
            {
                if (outputPosition != result.Length || remaining.Any(frequency => frequency != 0))
                {
                    throw new InvalidDataException("Decoded data does not match the frequency table.");
                }

                return result;
            }

            if (outputPosition >= result.Length || remaining[symbol] == 0)
            {
                throw new InvalidDataException("Decoded data does not match the frequency table.");
            }

            result[outputPosition++] = (byte)symbol;
            remaining[symbol]--;

            UInt128 start = low;
            high = (ulong)(start + range * (ulong)cumulative[symbol + 1] / (ulong)total - 1);
            low = (ulong)(start + range * (ulong)cumulative[symbol] / (ulong)total);

            while (true)
            {
                if (high >= Half)
                {
                    if (low >= Half)
                    {
                        low -= Half;
                        high -= Half;
                        code -= Half;
                    }
                    else if (low >= Quarter && high < ThreeQuarters)
                    {
                        low -= Quarter;
                        high -= Quarter;
                        code -= Quarter;
                    }
                    else
                    {
                        break;
                    }
                }

                low <<= 1;
                high = (high << 1) | 1;
                code = (code << 1) | (uint)bits.ReadBit();
            }
        }
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

    private static int FindSymbol(long[] cumulative, ulong value)
    {
        int left = 0;
        int right = 257;

        while (left + 1 < right)
        {
            int middle = (left + right) / 2;

            if ((ulong)cumulative[middle] <= value)
            {
                left = middle;
            }
            else
            {
                right = middle;
            }
        }

        return left;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage: ArithmeticDecompress <input.arc> <output>");
        Console.Error.WriteLine("   or: ArithmeticDecompress --text <Base64>");
    }

    private sealed class BitReader
    {
        private readonly byte[] _archive;
        private readonly int _offset;
        private readonly long _bitCount;
        private long _position;

        public BitReader(byte[] archive, int offset, long bitCount)
        {
            _archive = archive;
            _offset = offset;
            _bitCount = bitCount;
        }

        public int ReadBit()
        {
            if (_position >= _bitCount)
            {
                throw new InvalidDataException("Truncated arithmetic code.");
            }

            return BitAt(_position++);
        }

        public int BitAt(long index)
        {
            int byteIndex = _offset + (int)(index / 8);
            return (_archive[byteIndex] >> (7 - (int)(index % 8))) & 1;
        }
    }
}
