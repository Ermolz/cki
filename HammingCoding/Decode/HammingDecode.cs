using System.Text;

internal static class Program
{
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
                Console.Write(Utf8.GetString(Decode(Convert.FromBase64String(args[1]))));
                return 0;
            }

            byte[] restored = Decode(File.ReadAllBytes(args[0]));
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
                                          or InvalidDataException or FormatException)
        {
            Console.Error.WriteLine(exception switch
            {
                FileNotFoundException => "Error: file not found.",
                DirectoryNotFoundException => "Error: directory not found.",
                UnauthorizedAccessException => "Error: access denied.",
                InvalidDataException => exception.Message,
                DecoderFallbackException => "Error: invalid UTF-8.",
                FormatException => "Error: invalid Base64.",
                IOException => "Error: file I/O failed.",
                _ => "Error: invalid argument."
            });
            return 1;
        }
    }

    private static byte[] Decode(byte[] encoded)
    {
        if (encoded.Length % 2 != 0)
        {
            throw new InvalidDataException("Error: invalid encoded length.");
        }

        byte[] result = new byte[encoded.Length / 2];

        for (int index = 0; index < result.Length; index++)
        {
            int high = DecodeNibble(encoded[index * 2]);
            int low = DecodeNibble(encoded[index * 2 + 1]);
            result[index] = (byte)((high << 4) | low);
        }

        return result;
    }

    private static int DecodeNibble(byte code)
    {
        int syndrome = 0;
        int parity = 0;

        for (int position = 1; position <= 8; position++)
        {
            int bit = GetBit(code, position);
            parity ^= bit;

            if (position <= 7 && bit == 1)
            {
                syndrome ^= position;
            }
        }

        if (syndrome != 0 && parity == 0)
        {
            throw new InvalidDataException("Error: uncorrectable block.");
        }

        if (parity != 0)
        {
            int errorPosition = syndrome == 0 ? 8 : syndrome;
            code ^= (byte)(1 << (8 - errorPosition));
        }

        return (GetBit(code, 3) << 3) | (GetBit(code, 5) << 2)
               | (GetBit(code, 6) << 1) | GetBit(code, 7);
    }

    private static int GetBit(byte code, int position)
    {
        return (code >> (8 - position)) & 1;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage: HammingDecode <input.ham> [output]");
        Console.Error.WriteLine("   or: HammingDecode --text <Base64>");
    }
}

// Run from the repository root:
// dotnet run --project .\HammingCoding\Decode\HammingDecode.csproj -c Release -- .\HammingCoding\encoded.ham .\HammingCoding\restored.txt


// .\HammingCoding\IntroduceError.ps1 -InputPath .\HammingCoding\encoded.ham.txt -OutputPath .\HammingCoding\damaged.ham