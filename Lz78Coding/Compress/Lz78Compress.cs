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
        Dictionary<(int Prefix, byte Symbol), int> dictionary = new();
        BitWriter bits = new();
        int prefix = 0;

        foreach (byte symbol in input)
        {
            if (dictionary.TryGetValue((prefix, symbol), out int index))
            {
                prefix = index;
                continue;
            }

            int dictionaryCount = dictionary.Count + 1;
            bits.WriteBits(prefix, IndexWidth(dictionaryCount));
            bits.WriteBits(symbol, 8);
            dictionary.Add((prefix, symbol), dictionaryCount);
            prefix = 0;
        }

        if (prefix != 0)
        {
            bits.WriteBits(prefix, IndexWidth(dictionary.Count + 1));
        }

        byte[] payload = bits.ToArray();
        long archiveSize = 20L + payload.Length;

        if (archiveSize > Array.MaxLength)
        {
            throw new OverflowException();
        }

        using MemoryStream stream = new((int)archiveSize);
        using BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write((long)input.Length);
        writer.Write(bits.BitCount);
        writer.Write(payload);
        writer.Flush();
        return stream.ToArray();
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
        Console.Error.WriteLine("Usage: Lz78Compress <input> <output.lz78>");
        Console.Error.WriteLine("   or: Lz78Compress --text <text>");
    }

    private sealed class BitWriter
    {
        private readonly List<byte> _bytes = new();
        private byte _current;
        private int _occupied;

        public long BitCount { get; private set; }

        public void WriteBits(int value, int width)
        {
            for (int bit = width - 1; bit >= 0; bit--)
            {
                if (((value >> bit) & 1) != 0)
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

// dotnet run --project .\Lz78Coding\Compress\Lz78Compress.csproj -c Release -- .\input.txt .\Lz78Coding\compressed.lz78
