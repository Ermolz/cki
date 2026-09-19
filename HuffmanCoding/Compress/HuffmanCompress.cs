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

        if (args.Length != 2)
        {
            PrintUsage();
            return 1;
        }

        try
        {
            if (args[0] == "--text")
            {
                byte[] archive = Compress(Utf8.GetBytes(args[1]));
                Console.WriteLine(Convert.ToBase64String(archive));
                return 0;
            }

            byte[] input = File.ReadAllBytes(args[0]);
            byte[] compressed = Compress(input);

            using (FileStream output = new(args[1], FileMode.CreateNew, FileAccess.Write))
            {
                output.Write(compressed);
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

        Node? root = BuildTree(frequencies);
        string?[] codes = new string?[256];

        if (root is not null)
        {
            BuildCodes(root, string.Empty, codes);
        }

        long bitCount = 0;
        ushort symbolCount = 0;

        for (int symbol = 0; symbol < frequencies.Length; symbol++)
        {
            if (frequencies[symbol] == 0)
            {
                continue;
            }

            symbolCount++;
            bitCount += frequencies[symbol] * codes[symbol]!.Length;
        }

        long payloadSize = (bitCount + 7) / 8;
        long archiveSize = 22L + symbolCount * 9L + payloadSize;

        if (archiveSize > Array.MaxLength)
        {
            throw new OverflowException();
        }

        using MemoryStream stream = new((int)archiveSize);
        using BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write((long)input.Length);
        writer.Write(bitCount);
        writer.Write(symbolCount);

        for (int symbol = 0; symbol < frequencies.Length; symbol++)
        {
            if (frequencies[symbol] > 0)
            {
                writer.Write((byte)symbol);
                writer.Write(frequencies[symbol]);
            }
        }

        byte packedByte = 0;
        int occupiedBits = 0;

        foreach (byte symbol in input)
        {
            foreach (char bit in codes[symbol]!)
            {
                if (bit == '1')
                {
                    packedByte |= (byte)(1 << (7 - occupiedBits));
                }

                occupiedBits++;

                if (occupiedBits == 8)
                {
                    writer.Write(packedByte);
                    packedByte = 0;
                    occupiedBits = 0;
                }
            }
        }

        if (occupiedBits > 0)
        {
            writer.Write(packedByte);
        }

        writer.Flush();
        return stream.ToArray();
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

    private static void BuildCodes(Node node, string prefix, string?[] codes)
    {
        if (node.Symbol is int symbol)
        {
            codes[symbol] = prefix.Length == 0 ? "0" : prefix;
            return;
        }

        BuildCodes(node.Left!, prefix + "0", codes);
        BuildCodes(node.Right!, prefix + "1", codes);
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage: HuffmanCompress <input> <output.huf>");
        Console.Error.WriteLine("   or: HuffmanCompress --text <text>");
    }

    private sealed record Node(long Frequency, int MinimumSymbol, int? Symbol,
                               Node? Left = null, Node? Right = null);
}
