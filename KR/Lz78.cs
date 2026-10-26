using System.Globalization;
using System.Text;

internal static class Lz78Solution
{
    public sealed record Result(string Text, IReadOnlyList<string> Dictionary, int? TerminalReference);

    public static void Run()
    {
        const string input = "0101010100111100";
        const string code = "100010100100110100011001";

        Result encoded = Encode(input);
        Result decoded = Decode(code);

        Console.WriteLine("LZ-78");
        Console.WriteLine("a) " + encoded.Text);
        PrintDictionary("Dictionary a:", encoded);
        Console.WriteLine("b) " + decoded.Text);
        PrintDictionary("Dictionary b:", decoded);
    }

    public static Result Encode(string bits)
    {
        ValidateBinary(bits);
        var dictionary = new List<string> { string.Empty };
        var indices = new Dictionary<string, int>(StringComparer.Ordinal) { [string.Empty] = 0 };
        var code = new StringBuilder();
        int position = 0;
        int? terminalReference = null;

        while (position < bits.Length)
        {
            string phrase = string.Empty;
            int index = 0;

            while (position < bits.Length && indices.TryGetValue(phrase + bits[position], out int nextIndex))
            {
                phrase += bits[position++];
                index = nextIndex;
            }

            AppendIndex(code, index, IndexWidth(dictionary.Count));

            if (position == bits.Length)
            {
                terminalReference = index;
                break;
            }

            char nextBit = bits[position++];
            code.Append(nextBit);
            string nextPhrase = phrase + nextBit;
            indices.Add(nextPhrase, dictionary.Count);
            dictionary.Add(nextPhrase);
        }

        return new Result(code.ToString(), dictionary.AsReadOnly(), terminalReference);
    }

    public static Result Decode(string code)
    {
        ValidateBinary(code);
        var dictionary = new List<string> { string.Empty };
        var bits = new StringBuilder();
        int position = 0;
        int? terminalReference = null;

        while (position < code.Length)
        {
            int width = IndexWidth(dictionary.Count);
            if (code.Length - position < width)
            {
                throw new FormatException("Incomplete dictionary index.");
            }

            int index = 0;
            for (int i = 0; i < width; i++)
            {
                index = (index << 1) | (code[position++] - '0');
            }

            if (index >= dictionary.Count)
            {
                throw new FormatException("Unknown dictionary index.");
            }

            string phrase = dictionary[index];
            if (position == code.Length)
            {
                bits.Append(phrase);
                terminalReference = index;
                break;
            }

            string nextPhrase = phrase + code[position++];
            bits.Append(nextPhrase);
            dictionary.Add(nextPhrase);
        }

        return new Result(bits.ToString(), dictionary.AsReadOnly(), terminalReference);
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

    private static void AppendIndex(StringBuilder output, int index, int width)
    {
        for (int bit = width - 1; bit >= 0; bit--)
        {
            output.Append(((index >> bit) & 1) == 0 ? '0' : '1');
        }
    }

    private static void ValidateBinary(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        foreach (char bit in value)
        {
            if (bit is not ('0' or '1'))
            {
                throw new FormatException("Input must contain only binary digits.");
            }
        }
    }

    private static void PrintDictionary(string title, Result result)
    {
        Console.WriteLine(title);
        for (int i = 0; i < result.Dictionary.Count; i++)
        {
            string phrase = i == 0 ? "<empty>" : result.Dictionary[i];
            Console.WriteLine(i.ToString(CultureInfo.InvariantCulture) + ": " + phrase);
        }

        if (result.TerminalReference is int terminalReference)
        {
            Console.WriteLine("Terminal reference: " + terminalReference.ToString(CultureInfo.InvariantCulture));
        }
    }
}
