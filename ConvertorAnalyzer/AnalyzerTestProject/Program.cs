using AnalyzerTestProject.Shared;
using Microsoft.VisualBasic;
using NUnit.Framework;

namespace AnalyzerTestProject;

public static class Program
{
    static void Main(string[] args)
    {
        var from = new ConvertFrom();
        var to = new ConvertTo();

        //test.TestScenario(from, to);
    }
}

public record ConvertFrom
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
    public string Prop { get; set; } = "Prop";
    public string Prop3 { get; set; } = "Prop3";
}

public abstract class Converter<TFrom, TTo>
{
    //public abstract void TestScenario(TFrom expected, TTo tested);
}

public class Test1 : Converter<ConvertFrom, ConvertTo>
{

}
public class Test2 : Converter<Msg.PhoneCall, ConvertTo>
{

}