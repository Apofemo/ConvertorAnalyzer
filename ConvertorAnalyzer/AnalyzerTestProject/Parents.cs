using System.Runtime.Serialization;

namespace AnalyzerTestProject;


public class ChildProp
{
    public int aaa { get; set; }
}

public class ConvertToParent : ConvertToParentParent
{
    public int IntPropParent { get; set; }
    public string StringPropParent { get; set; } = "StringProp";
}

public class ConvertToParentParent : object, System.Runtime.Serialization.IExtensibleDataObject
{
    public int IntPropParentParent { get; set; }
    public string StringPropParentParent { get; set; } = "StringProp";
    public ExtensionDataObject? ExtensionData { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
}