using System.Globalization;

internal static class HuffmanSolution
{
    public static void Run()
    {
        var weights = new Dictionary<char, int>
        {
            ['a'] = 9,
            ['b'] = 7,
            ['c'] = 11,
            ['d'] = 13,
            ['e'] = 5,
            ['f'] = 13,
            ['g'] = 8,
            ['h'] = 6,
            ['i'] = 12,
            ['j'] = 3,
            ['k'] = 13
        };

        var codes = BuildCodes(weights);
        double totalWeight = weights.Values.Sum();
        double entropy = 0;
        double averageLength = 0;

        Console.WriteLine("Huffman");
        foreach (var (symbol, weight) in weights.OrderBy(pair => pair.Key))
        {
            double probability = weight / totalWeight;
            entropy -= probability * Math.Log2(probability);
            averageLength += probability * codes[symbol].Length;
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{symbol} {probability:F2} {codes[symbol]}"));
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Entropy: {entropy:F9}"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Average length: {averageLength:F2}"));
    }

    private static Dictionary<char, string> BuildCodes(IReadOnlyDictionary<char, int> weights)
    {
        var queue = new PriorityQueue<Node, (long Weight, char MinimumSymbol)>();
        foreach (var (symbol, weight) in weights)
        {
            if (weight <= 0)
            {
                throw new ArgumentException("Weights must be positive.", nameof(weights));
            }

            var leaf = new Node(symbol, weight);
            queue.Enqueue(leaf, (leaf.Weight, leaf.MinimumSymbol));
        }

        while (queue.Count > 1)
        {
            var left = queue.Dequeue();
            var right = queue.Dequeue();
            var parent = new Node(left, right);
            queue.Enqueue(parent, (parent.Weight, parent.MinimumSymbol));
        }

        var codes = new Dictionary<char, string>();
        if (queue.Count > 0)
        {
            CollectCodes(queue.Dequeue(), "", codes);
        }

        return codes;
    }

    private static void CollectCodes(Node node, string prefix, Dictionary<char, string> codes)
    {
        if (node.Symbol is char symbol)
        {
            codes.Add(symbol, prefix.Length == 0 ? "0" : prefix);
            return;
        }

        if (node.Left is not null)
        {
            CollectCodes(node.Left, prefix + "0", codes);
        }

        if (node.Right is not null)
        {
            CollectCodes(node.Right, prefix + "1", codes);
        }
    }

    private sealed class Node
    {
        public char? Symbol { get; }
        public long Weight { get; }
        public char MinimumSymbol { get; }
        public Node? Left { get; }
        public Node? Right { get; }

        public Node(char symbol, long weight)
        {
            Symbol = symbol;
            Weight = weight;
            MinimumSymbol = symbol;
        }

        public Node(Node left, Node right)
        {
            Weight = left.Weight + right.Weight;
            MinimumSymbol = left.MinimumSymbol < right.MinimumSymbol
                ? left.MinimumSymbol
                : right.MinimumSymbol;
            Left = left;
            Right = right;
        }
    }
}
