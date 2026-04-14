using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace ExpandableCalculator
{
    public static class Calculator
    {
        private static readonly Dictionary<string, (int precedence, bool leftAssoc, Func<double, double, double> op)> Operators =
            new Dictionary<string, (int, bool, Func<double, double, double>)>
        {
            {"+", (1, true, (a,b) => a + b)},
            {"-", (1, true, (a,b) => a - b)},
            {"*", (2, true, (a,b) => a * b)},
            {"/", (2, true, (a,b) => a / b)},
            {"%", (2, true, (a,b) => (a / b) * 100)}
        };

        public static (double value, string fraction, bool isPercent) Evaluate(string expr)
        {
            // Handle: "What percent of 25 is 5?"
            var match = Regex.Match(
                expr,
                @"what\s+percent\s+of\s+([\d\.]+)\s+is\s+([\d\.]+)",
                RegexOptions.IgnoreCase
            );
            // Handle: "Convert the following decimal to a percent: 1.15"
            var percentConvert = Regex.Match(
                expr,
                @"convert.*decimal.*percent[:\s]*([\d\.]+)|([\d\.]+)\s*(to|as)\s*percent",
                RegexOptions.IgnoreCase
            );

            if (percentConvert.Success)
            {
                string numStr = percentConvert.Groups[1].Success
                    ? percentConvert.Groups[1].Value
                    : percentConvert.Groups[2].Value;

                double number = double.Parse(numStr, CultureInfo.InvariantCulture);
                double percent = Math.Round(number * 100, 2);

                return (percent, percent.ToString(CultureInfo.InvariantCulture), true);
            }

            // Handle: "Estimate what it will cost to purchase 50 new computers if an average new computer costs $2,000."
            var estimateCostMatch = Regex.Match(
                expr,
                @"estimate.*?(\d+(?:\.\d+)?).*?\$?(\d+(?:\.\d+)?)",
                RegexOptions.IgnoreCase
            );
            if (estimateCostMatch.Success && expr.IndexOf("computer", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                double quantity = double.Parse(estimateCostMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                double unitCost = double.Parse(estimateCostMatch.Groups[2].Value, CultureInfo.InvariantCulture);
                double estimate = Math.Round(quantity * unitCost, 2);
                return (estimate, estimate.ToString(CultureInfo.InvariantCulture), false);
            }

            // Handle: "It was estimated that a job would cost $1,200, but it ended up costing $1,400."
            if (expr.IndexOf("estimate", StringComparison.OrdinalIgnoreCase) >= 0 &&
                (expr.IndexOf("actual", StringComparison.OrdinalIgnoreCase) >= 0 || expr.IndexOf("ended", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                var accuracyValues = ExtractNumbers(expr);
                if (accuracyValues.Count >= 2)
                {
                    double estimateValue = accuracyValues[0];
                    double actualValue = accuracyValues[1];
                    double accuracy = Math.Round(((actualValue - estimateValue) / estimateValue) * 100, 2);
                    return (accuracy, accuracy.ToString(CultureInfo.InvariantCulture), true);
                }
            }

            // Handle: "average of 120, 80, 100"
            var averageMatch = Regex.Match(
                expr,
                @"(?:average|mean)\s+(?:of|of the|size of|value of)?\s*(.+)",
                RegexOptions.IgnoreCase
            );
            if (averageMatch.Success)
            {
                var values = ExtractNumbers(averageMatch.Groups[1].Value);
                if (values.Count >= 2)
                {
                    double average = Math.Round(values.Average(), 2);
                    bool averageHadFraction = averageMatch.Groups[1].Value.Contains("/");
                    string fraction = averageHadFraction ? ToFraction(average) : average.ToString(CultureInfo.InvariantCulture);
                    return (average, fraction, false);
                }
            }

            if (match.Success)
            {
                double whole = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                double part = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                double percent = Math.Round((part / whole) * 100, 2);
                return (percent, percent.ToString(CultureInfo.InvariantCulture), true);
            }

            bool inputHadFraction = expr.Contains("/");
            var rpn = ToRPN(expr);
            double val = EvalRPN(rpn);

            val = Math.Round(val, 2);

            string frac = inputHadFraction ? ToFraction(val) : val.ToString();
            return (val, frac, false);
        }

        private static double ParseNumber(string token)
        {
            if (token.Contains("/"))
            {
                var parts = token.Split('/');
                double num = double.Parse(parts[0], CultureInfo.InvariantCulture);
                double den = double.Parse(parts[1], CultureInfo.InvariantCulture);
                return num / den;
            }
            return double.Parse(token, CultureInfo.InvariantCulture);
        }

        private static List<double> ExtractNumbers(string input)
        {
            var values = new List<double>();
            string cleaned = input.Replace("$", string.Empty).Replace(",", string.Empty);
            foreach (Match match in Regex.Matches(cleaned, @"-?\d+(?:\.\d+)?(?:/\d+)?"))
            {
                values.Add(ParseNumber(match.Value));
            }
            return values;
        }

        private static List<string> ToRPN(string expr)
        {
            var output = new List<string>();
            var ops = new Stack<string>();
            int i = 0;

            while (i < expr.Length)
            {
                if (char.IsWhiteSpace(expr[i])) { i++; continue; }

                if (char.IsDigit(expr[i]))
                {
                    int start = i;
                    while (i < expr.Length && (char.IsDigit(expr[i]) || expr[i] == '.' || expr[i] == '/')) i++;
                    output.Add(expr.Substring(start, i - start));
                    continue;
                }

                string c = expr[i].ToString();

                if (Operators.ContainsKey(c))
                {
                    while (ops.Count > 0 && Operators.ContainsKey(ops.Peek()))
                    {
                        var o1 = Operators[c];
                        var o2 = Operators[ops.Peek()];

                        if ((o1.leftAssoc && o1.precedence <= o2.precedence))
                            output.Add(ops.Pop());
                        else break;
                    }
                    ops.Push(c);
                }
                else if (c == "(") ops.Push(c);
                else if (c == ")")
                {
                    while (ops.Count > 0 && ops.Peek() != "(") output.Add(ops.Pop());
                    ops.Pop();
                }

                i++;
            }

            while (ops.Count > 0) output.Add(ops.Pop());
            return output;
        }

        private static double EvalRPN(List<string> rpn)
        {
            var stack = new Stack<double>();

            foreach (var token in rpn)
            {
                if (Operators.ContainsKey(token))
                {
                    double b = stack.Pop();
                    double a = stack.Pop();
                    stack.Push(Operators[token].op(a, b));
                }
                else stack.Push(ParseNumber(token));
            }
            return stack.Pop();
        }

        private static string ToFraction(double value)
        {
            int sign = value < 0 ? -1 : 1;
            value = Math.Abs(value);

            int bestN = 1, bestD = 1;
            double bestErr = Math.Abs(value - 1);

            for (int d = 1; d <= 5000; d++)
            {
                int n = (int)Math.Round(value * d);
                double err = Math.Abs(value - (double)n / d);
                if (err < bestErr)
                {
                    bestErr = err;
                    bestN = n;
                    bestD = d;
                }
            }
            return $"{sign * bestN}/{bestD}";
        }
    }

    class Program
    {
        static void Main()
        {
            Console.WriteLine("Fraction/Decimal Calculator");
            Console.WriteLine("Type 'exit' to quit.\n");

            while (true)
            {
                Console.Write("Enter expression: ");
#pragma warning disable CS8600
                string input = Console.ReadLine();
#pragma warning restore CS8600

#pragma warning disable CS8602
                if (input.ToLower() == "exit") break;
#pragma warning restore CS8602

                try
                {
                    var result = Calculator.Evaluate(input);

                    if (result.isPercent)
                        Console.WriteLine($"= {result.value}%\n");
                    else
                        Console.WriteLine($"= {result.fraction} (≈ {result.value})\n");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}\n");
                }
            }
        }
    }
}
