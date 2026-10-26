using System.Text;

internal static class Program
{
    private static readonly byte[] Magic = "SFC1"u8.ToArray();
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
                byte[] archive = Convert.FromBase64String(args[1]);
                Console.Write(Utf8.GetString(Decompress(archive)));
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
            throw new InvalidDataException("Invalid SFC1 archive.");
        }

        long originalLength = reader.ReadInt64();
        long bitCount = reader.ReadInt64();
        int symbolCount = reader.ReadUInt16();

        if (originalLength < 0 || originalLength > Array.MaxLength || bitCount < 0 || symbolCount > 256)
        {
            throw new InvalidDataException("Invalid archive header or unsupported file size.");
        }

        long[] frequencies = new long[256];
        long totalFrequency = 0;
        int previousSymbol = -1;

        for (int index = 0; index < symbolCount; index++)
        {
            int symbol = reader.ReadByte();
            long frequency = reader.ReadInt64();

            if (symbol <= previousSymbol || frequency <= 0 || frequency > originalLength - totalFrequency)
            {
                throw new InvalidDataException("Invalid frequency table.");
            }

            frequencies[symbol] = frequency;
            totalFrequency += frequency;
            previousSymbol = symbol;
        }

        if (totalFrequency != originalLength)
        {
            throw new InvalidDataException("Frequency sum does not match the original size.");
        }

        Node? root = BuildTree(frequencies);
        long expectedBitCount = root is null ? 0 : CountBits(root, 0);

        if (bitCount != expectedBitCount)
        {
            throw new InvalidDataException("Bit count does not match the frequency table.");
        }

        long payloadSize = (bitCount + 7) / 8;

        if (stream.Length - stream.Position != payloadSize)
        {
            throw new InvalidDataException("Invalid payload length.");
        }

        int payloadOffset = (int)stream.Position;
        int paddingBits = (int)((8 - bitCount % 8) % 8);

        if (payloadSize > 0 && (archive[^1] & ((1 << paddingBits) - 1)) != 0)
        {
            throw new InvalidDataException("Invalid padding bits.");
        }

        byte[] result = new byte[(int)originalLength];

        if (root is null)
        {
            return result;
        }

        if (root.Symbol is int onlySymbol)
        {
            for (int index = payloadOffset; index < archive.Length; index++)
            {
                if (archive[index] != 0)
                {
                    throw new InvalidDataException("Invalid single-symbol code.");
                }
            }

            Array.Fill(result, (byte)onlySymbol);
            return result;
        }

        Node current = root;
        int outputPosition = 0;
        long[] remainingFrequencies = (long[])frequencies.Clone();

        for (long bitIndex = 0; bitIndex < bitCount; bitIndex++)
        {
            int byteIndex = payloadOffset + (int)(bitIndex / 8);
            int bit = (archive[byteIndex] >> (7 - (int)(bitIndex % 8))) & 1;
            current = bit == 0 ? current.Left! : current.Right!;

            if (current.Symbol is not int symbol)
            {
                continue;
            }

            if (outputPosition >= result.Length || remainingFrequencies[symbol] == 0)
            {
                throw new InvalidDataException("Decoded bytes do not match the frequency table.");
            }

            result[outputPosition++] = (byte)symbol;
            remainingFrequencies[symbol]--;
            current = root;
        }

        if (!ReferenceEquals(current, root) || outputPosition != result.Length
            || remainingFrequencies.Any(frequency => frequency != 0))
        {
            throw new InvalidDataException("Incomplete encoded data.");
        }

        return result;
    }

    private static Node? BuildTree(long[] frequencies)
    {
        Node[] symbols = Enumerable.Range(0, frequencies.Length)
            .Where(symbol => frequencies[symbol] > 0)
            .Select(symbol => new Node(frequencies[symbol], symbol))
            .OrderByDescending(node => node.Frequency)
            .ThenBy(node => node.Symbol)
            .ToArray();

        return symbols.Length == 0 ? null : BuildSubtree(symbols, 0, symbols.Length - 1);
    }

    private static Node BuildSubtree(Node[] symbols, int start, int end)
    {
        if (start == end)
        {
            return symbols[start];
        }

        long totalFrequency = 0;

        for (int index = start; index <= end; index++)
        {
            totalFrequency += symbols[index].Frequency;
        }

        long leftFrequency = 0;
        long bestDifference = long.MaxValue;
        int split = start;

        for (int index = start; index < end; index++)
        {
            leftFrequency += symbols[index].Frequency;
            long difference = Math.Abs(totalFrequency - 2 * leftFrequency);

            if (difference < bestDifference)
            {
                bestDifference = difference;
                split = index;
            }
        }

        Node left = BuildSubtree(symbols, start, split);
        Node right = BuildSubtree(symbols, split + 1, end);
        return new Node(totalFrequency, null, left, right);
    }

    private static long CountBits(Node node, int depth)
    {
        if (node.Symbol is not null)
        {
            return node.Frequency * Math.Max(depth, 1);
        }

        return CountBits(node.Left!, depth + 1) + CountBits(node.Right!, depth + 1);
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage: ShannonFanoDecompress <input.sfc> <output>");
        Console.Error.WriteLine("   or: ShannonFanoDecompress --text <Base64>");
    }

    private sealed record Node(long Frequency, int? Symbol,
                               Node? Left = null, Node? Right = null);
}

// dotnet run --project .\ShannonFanoCoding\Decompress\ShannonFanoDecompress.csproj -c Release -- .\ShannonFanoCoding\compressed.sfc .\ShannonFanoCoding\restored.txt
