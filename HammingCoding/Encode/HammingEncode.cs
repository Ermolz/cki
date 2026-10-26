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

        if (args.Length != 2)
        {
            PrintUsage();
            return 1;
        }

        try
        {
            if (args[0] == "--text")
            {
                Console.WriteLine(Convert.ToBase64String(Encode(Utf8.GetBytes(args[1]))));
                return 0;
            }

            byte[] encoded = Encode(File.ReadAllBytes(args[0]));

            using (FileStream output = new(args[1], FileMode.CreateNew, FileAccess.Write))
            {
                output.Write(encoded);
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

    private static byte[] Encode(byte[] input)
    {
        if (input.Length > Array.MaxLength / 2)
        {
            throw new OverflowException();
        }

        byte[] result = new byte[input.Length * 2];

        for (int index = 0; index < input.Length; index++)
        {
            result[index * 2] = EncodeNibble(input[index] >> 4);
            result[index * 2 + 1] = EncodeNibble(input[index] & 15);
        }

        return result;
    }

    private static byte EncodeNibble(int data)
    {
        int d1 = (data >> 3) & 1;
        int d2 = (data >> 2) & 1;
        int d3 = (data >> 1) & 1;
        int d4 = data & 1;
        int p1 = d1 ^ d2 ^ d4;
        int p2 = d1 ^ d3 ^ d4;
        int p4 = d2 ^ d3 ^ d4;
        int p8 = p1 ^ p2 ^ d1 ^ p4 ^ d2 ^ d3 ^ d4;

        return (byte)((p1 << 7) | (p2 << 6) | (d1 << 5) | (p4 << 4)
                      | (d2 << 3) | (d3 << 2) | (d4 << 1) | p8);
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage: HammingEncode <input> <output.ham>");
        Console.Error.WriteLine("   or: HammingEncode --text <text>");
    }
}

// Run from the repository root:
// dotnet run --project .\HammingCoding\Encode\HammingEncode.csproj -c Release -- .\input.txt .\HammingCoding\encoded.ham
