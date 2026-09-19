using System.Text;

internal static class Program
{
    private static readonly byte[] Magic = "HUF1"u8.ToArray();
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
                byte[] archive = Convert.FromBase64String(args[1]);
                Console.Write(Utf8.GetString(Decompress(archive)));
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
            throw new InvalidDataException("Invalid HUF1 archive.");
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
        PriorityQueue<Node, (long Frequency, int MinimumSymbol)> queue = new();

        for (int symbol = 0; symbol < frequencies.Length; symbol++)
        {
            if (frequencies[symbol] > 0)
            {
                Node leaf = new(frequencies[symbol], symbol, symbol);
                queue.Enqueue(leaf, (leaf.Frequency, leaf.MinimumSymbol));
            }
        }

        while (queue.Count > 1)
        {
            Node left = queue.Dequeue();
            Node right = queue.Dequeue();
            Node parent = new(left.Frequency + right.Frequency,
                              Math.Min(left.MinimumSymbol, right.MinimumSymbol),
                              null, left, right);
            queue.Enqueue(parent, (parent.Frequency, parent.MinimumSymbol));
        }

        return queue.Count == 0 ? null : queue.Dequeue();
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
        Console.Error.WriteLine("Usage: HuffmanDecompress <input.huf> [output]");
        Console.Error.WriteLine("   or: HuffmanDecompress --text <Base64>");
    }

    private sealed record Node(long Frequency, int MinimumSymbol, int? Symbol,
                               Node? Left = null, Node? Right = null);
}
