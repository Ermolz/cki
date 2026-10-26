using System.Text;

internal static class Program
{
    private static readonly byte[] Magic = "LZ78"u8.ToArray();
    private static readonly UTF8Encoding Utf8 = new(false, true);

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Utf8;

        if (args.Length == 1 && args[0] is "--help" or "-h")
        {
            PrintUsage();
            return 0;
        }

        if (args.Length is < 1 or > 2 || args.Length == 1 && args[0] == "--text")
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
            string outputPath = args.Length == 2 ? args[1] : args[0] + ".restored";

            using (FileStream output = new(outputPath, FileMode.CreateNew, FileAccess.Write))
            {
                output.Write(restored);
            }

            Console.WriteLine($"Saved: {outputPath}");
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
            throw new InvalidDataException("Invalid LZ78 archive.");
        }

        long originalLength = reader.ReadInt64();
        long bitCount = reader.ReadInt64();

        if (originalLength < 0 || originalLength > Array.MaxLength || bitCount < 0)
        {
            throw new InvalidDataException("Invalid archive header.");
        }

        long payloadSize = bitCount / 8 + (bitCount % 8 == 0 ? 0 : 1);

        if (stream.Length - stream.Position != payloadSize)
        {
            throw new InvalidDataException("Invalid payload length.");
        }

        int paddingBits = (int)((8 - bitCount % 8) % 8);

        if (paddingBits > 0 && (archive[^1] & ((1 << paddingBits) - 1)) != 0)
        {
            throw new InvalidDataException("Invalid padding bits.");
        }

        BitReader bits = new(archive, (int)stream.Position, bitCount);
        List<Entry> dictionary = new() { new(0, 0, 0) };
        byte[] result = new byte[(int)originalLength];
        int outputPosition = 0;

        while (bits.Remaining > 0)
        {
            int prefix = bits.ReadBits(IndexWidth(dictionary.Count));

            if (prefix >= dictionary.Count)
            {
                throw new InvalidDataException("Unknown dictionary index.");
            }

            if (bits.Remaining == 0)
            {
                if (prefix == 0)
                {
                    throw new InvalidDataException("Empty terminal reference.");
                }

                WritePhrase(prefix, dictionary, result, ref outputPosition);
                break;
            }

            byte symbol = (byte)bits.ReadBits(8);
            int length = dictionary[prefix].Length + 1;
            dictionary.Add(new Entry(prefix, symbol, length));
            WritePhrase(dictionary.Count - 1, dictionary, result, ref outputPosition);
        }

        if (outputPosition != result.Length)
        {
            throw new InvalidDataException("Decoded length does not match the header.");
        }

        return result;
    }

    private static void WritePhrase(int index, List<Entry> dictionary, byte[] result,
                                    ref int outputPosition)
    {
        int length = dictionary[index].Length;

        if (length > result.Length - outputPosition)
        {
            throw new InvalidDataException("Decoded data exceeds the original length.");
        }

        int end = outputPosition + length;
        int position = end - 1;

        while (index != 0)
        {
            Entry entry = dictionary[index];
            result[position--] = entry.Symbol;
            index = entry.Prefix;
        }

        outputPosition = end;
    }

    private static int IndexWidth(int dictionaryCount)
    {
        int width = 0;

        for (int value = dictionaryCount - 1; value > 0; value >>= 1)
        {
            width++;
        }

        return width;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage: Lz78Decompress <input.lz78> [output]");
        Console.Error.WriteLine("   or: Lz78Decompress --text <Base64>");
    }

    private readonly record struct Entry(int Prefix, byte Symbol, int Length);

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

        public long Remaining => _bitCount - _position;

        public int ReadBits(int width)
        {
            if (Remaining < width)
            {
                throw new InvalidDataException("Truncated LZ78 code.");
            }

            int value = 0;

            for (int bit = 0; bit < width; bit++)
            {
                int byteIndex = _offset + (int)(_position / 8);
                int shift = 7 - (int)(_position % 8);
                value = (value << 1) | ((_archive[byteIndex] >> shift) & 1);
                _position++;
            }

            return value;
        }
    }
}

// dotnet run --project .\Lz78Coding\Decompress\Lz78Decompress.csproj -c Release -- .\Lz78Coding\compressed.lz78 .\Lz78Coding\restored.txt
