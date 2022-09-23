using NUnit.Framework;

public static class Program
{
    static void Main(string[] args)
    {
        var from = new ConvertFrom();
        var to = new ConvertTo();

        var test = new Test();
        //test.TestScenario(from, to);
    }
}

public class ConvertFrom
{
    public int IntProp { get; set; }
    public string StringProp { get; set; } = "StringProp";
    public string caseSens { get; set; } = "caseSens";
    public string ContainsFrom { get; set; } = "ContainsFrom";
    public string ContainsToContains { get; set; } = "ContainsToContains";
    public string MismatchFrom { get; set; } = "MismatchFrom";
}

public class ConvertTo
{
    public int IntProp { get; set; }
    public string StringProp { get; set; } = "StringProp";
    public string CaseSens { get; set; } = "CaseSens";
    public string ContainsFromContains { get; set; } = "ContainsFromContains";
    public string ContainsTo { get; set; } = "ContainsTo";
    public string MismatchTo { get; set; } = "MismatchTo";
}

public abstract class Converter<TFrom, TTo>
{
    //public abstract void TestScenario(TFrom expected, TTo tested);
}

public class Test : Converter<ConvertFrom, ConvertTo>
{
    public void TestScenarioo(ConvertFrom expected, ConvertTo tested)
    {
        Console.WriteLine(expected.IntProp + " -> " + tested.IntProp + "\n");
        Console.WriteLine(expected.StringProp + " -> " + tested.StringProp);
    }
}