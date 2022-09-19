using System.Net.Http.Headers;

public static class Program
{
    static void Main(string[] args)
    {
        var from = new ConvertFrom();
        var to = new ConvertTo();

        var test = new Test();
        test.TestScenario(from, to);
    }
}

public class ConvertFrom
{
    public int IntProp { get; set; }
    public string StringProp { get; set; } = "StringProp";
}

public class ConvertTo
{
    public int IntProp { get; set; }
    public string StringProp { get; set; } = "StringProp";
}

public abstract class Converter<TFrom, TTo>
{
    public abstract void TestScenario(TFrom expected, TTo tested);
}

public class Test : Converter<ConvertFrom, ConvertTo>
{
    public override void TestScenario(ConvertFrom expected, ConvertTo tested)
    {
        Console.WriteLine(expected.IntProp + " -> " + tested.IntProp + "\n");
        Console.WriteLine(expected.StringProp + " -> " + tested.StringProp);
    }
}