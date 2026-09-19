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

            Console.WriteLine($"Восстановленный файл: {Path.GetFullPath(args[1])}");
            Console.WriteLine($"Восстановлено: {restored.Length} байт.");
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                          or ArgumentException or NotSupportedException
                                          or InvalidDataException or FormatException or OverflowException)
        {
            Console.Error.WriteLine($"Ошибка: {exception.Message}");
            return 1;
        }
    }

    private static byte[] Decompress(byte[] archive)
    {
        using MemoryStream stream = new(archive, writable: false);
        using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: true);

        if (!reader.ReadBytes(Magic.Length).AsSpan().SequenceEqual(Magic))
        {
            throw new InvalidDataException("Это не архив формата SFC1.");
        }

        long originalLength = reader.ReadInt64();
        long bitCount = reader.ReadInt64();
        int symbolCount = reader.ReadUInt16();

        if (originalLength < 0 || originalLength > Array.MaxLength || bitCount < 0 || symbolCount > 256)
        {
            throw new InvalidDataException("Некорректный заголовок архива или неподдерживаемый размер файла.");
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
                throw new InvalidDataException("Некорректная таблица частот.");
            }

            frequencies[symbol] = frequency;
            totalFrequency += frequency;
            previousSymbol = symbol;
        }

        if (totalFrequency != originalLength)
        {
            throw new InvalidDataException("Сумма частот не совпадает с исходным размером.");
        }

        Node? root = BuildTree(frequencies);
        long expectedBitCount = root is null ? 0 : CountBits(root, 0);

        if (bitCount != expectedBitCount)
        {
            throw new InvalidDataException("Число битов не соответствует таблице частот.");
        }

        long payloadSize = (bitCount + 7) / 8;

        if (stream.Length - stream.Position != payloadSize)
        {
            throw new InvalidDataException("Данные архива обрезаны или содержат лишние байты.");
        }

        int payloadOffset = (int)stream.Position;
        int paddingBits = (int)((8 - bitCount % 8) % 8);

        if (payloadSize > 0 && (archive[^1] & ((1 << paddingBits) - 1)) != 0)
        {
            throw new InvalidDataException("Некорректные дополняющие биты последнего байта.");
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
                    throw new InvalidDataException("Некорректный код единственного символа.");
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
                throw new InvalidDataException("Декодированные символы не соответствуют таблице частот.");
            }

            result[outputPosition++] = (byte)symbol;
            remainingFrequencies[symbol]--;
            current = root;
        }

        if (!ReferenceEquals(current, root) || outputPosition != result.Length
            || remainingFrequencies.Any(frequency => frequency != 0))
        {
            throw new InvalidDataException("Битовая последовательность не содержит полный исходный файл.");
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
        Console.Error.WriteLine("Восстановление файла: ShannonFanoDecompress <файл.sfc> <новый выходной файл>");
        Console.Error.WriteLine("Восстановление строки: ShannonFanoDecompress --text \"Base64\"");
        Console.Error.WriteLine("Для строки результат выводится как UTF-8. Существующий выходной файл не перезаписывается.");
    }

    private sealed record Node(long Frequency, int? Symbol,
                               Node? Left = null, Node? Right = null);
}
