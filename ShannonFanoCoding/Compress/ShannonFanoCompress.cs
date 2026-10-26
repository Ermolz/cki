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
            throw new IOException("Archive is too large for in-memory processing.");
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
        Console.Error.WriteLine("Usage: ShannonFanoCompress <input> <output.sfc>");
        Console.Error.WriteLine("   or: ShannonFanoCompress --text <text>");
    }

    private sealed record Node(long Frequency, int? Symbol,
                               Node? Left = null, Node? Right = null);
}

// dotnet run --project .\ShannonFanoCoding\Compress\ShannonFanoCompress.csproj -c Release -- .\input.txt .\ShannonFanoCoding\compressed.sfc
