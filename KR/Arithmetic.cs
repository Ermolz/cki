using System.Globalization;
using System.Text;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

string mode = args.Length == 0 ? "all" : args[0].ToLowerInvariant();

if (args.Length > 1 || mode is not ("all" or "huffman" or "arithmetic" or "lz78"))
{
    Console.Error.WriteLine("Usage: KR [all|huffman|arithmetic|lz78]");
    return 1;
}

if (mode is "all" or "huffman")
{
    HuffmanSolution.Run();
}

if (mode == "all")
{
    Console.WriteLine();
}

if (mode is "all" or "arithmetic")
{
    ArithmeticSolution.Run();
}

if (mode == "all")
{
    Console.WriteLine();
}

if (mode is "all" or "lz78")
{
    Lz78Solution.Run();
}

return 0;

internal static class ArithmeticSolution
{
    private static readonly Symbol[] Alphabet =
    [
        new('a', 0.10m),
        new('b', 0.29m),
        new('c', 0.26m),
        new('d', 0.13m),
        new('e', 0.22m)
    ];

    public static void Run()
    {
        Range[] model = BuildModel(Alphabet);
        (decimal low, decimal high) = Encode("cbdbb", model);
        (string bits, decimal value) = ChooseCode(low, high);
        string restored = Decode("111100000110010", 5, model);

        Console.WriteLine("Arithmetic");
        Console.WriteLine($"a) {bits}");
        Console.WriteLine($"Interval: [{Format(low)}, {Format(high)})");
        Console.WriteLine($"Value: {Format(value)}");
        Console.WriteLine($"b) {restored}");
    }

    private static Range[] BuildModel(Symbol[] alphabet)
    {
        List<Range> model = new();
        HashSet<char> characters = new();
        decimal cumulative = 0;

        foreach (Symbol symbol in alphabet)
        {
            if (symbol.Probability <= 0 || !characters.Add(symbol.Character))
            {
                throw new ArgumentException("Invalid alphabet.");
            }

            decimal next = cumulative + symbol.Probability;
            model.Add(new Range(symbol.Character, cumulative, next));
            cumulative = next;
        }

        if (cumulative != 1)
        {
            throw new ArgumentException("Probabilities must sum to one.");
        }

        return model.ToArray();
    }

    private static (decimal Low, decimal High) Encode(string text, Range[] model)
    {
        decimal low = 0;
        decimal high = 1;

        foreach (char character in text)
        {
            Range symbol = model.FirstOrDefault(item => item.Character == character)
                ?? throw new ArgumentException("Unknown symbol.");
            decimal width = high - low;
            decimal start = low;
            low = start + width * symbol.Low;
            high = start + width * symbol.High;

            if (low >= high)
            {
                throw new InvalidOperationException("Precision limit exceeded.");
            }
        }

        return (low, high);
    }

    private static (string Bits, decimal Value) ChooseCode(decimal low, decimal high)
    {
        decimal scale = 1;

        for (int length = 1; length <= 63; length++)
        {
            scale *= 2;
            decimal numerator = decimal.Ceiling(low * scale);

            if ((numerator + 1) / scale <= high)
            {
                string bits = Convert.ToString((long)numerator, 2).PadLeft(length, '0');
                return (bits, numerator / scale);
            }
        }

        throw new InvalidOperationException("Code is too long.");
    }

    private static string Decode(string bits, int length, Range[] model)
    {
        if (bits.Length == 0 || length < 0)
        {
            throw new ArgumentException("Invalid code or length.");
        }

        decimal value = 0;
        decimal position = 0.5m;

        foreach (char bit in bits)
        {
            if (bit is not ('0' or '1'))
            {
                throw new ArgumentException("Code must be binary.");
            }

            if (bit == '1')
            {
                value += position;
            }

            position /= 2;
        }

        decimal low = 0;
        decimal high = 1;
        StringBuilder restored = new(length);

        for (int index = 0; index < length; index++)
        {
            decimal width = high - low;
            bool found = false;

            foreach (Range symbol in model)
            {
                decimal nextLow = low + width * symbol.Low;
                decimal nextHigh = low + width * symbol.High;

                if (value >= nextLow && value < nextHigh)
                {
                    restored.Append(symbol.Character);
                    low = nextLow;
                    high = nextHigh;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                throw new InvalidOperationException("Invalid arithmetic code.");
            }
        }

        return restored.ToString();
    }

    private static string Format(decimal value) =>
        value.ToString("0.############################", CultureInfo.InvariantCulture);

    private sealed record Symbol(char Character, decimal Probability);

    private sealed record Range(char Character, decimal Low, decimal High);
}
