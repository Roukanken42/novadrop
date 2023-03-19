namespace Vezel.Novadrop.Schema;

internal interface IInferableNode
{
    internal interface IInferableKeys
    {
        bool HasAttributeNames { get; }

        IEnumerable<string> AttributeNames { get; }
    }

    internal interface IInferableAttribute
    {
        string Key { get; }

        DataCenterTypeCode TypeCode { get; }
    }

    string Name { get; }

    IInferableKeys? Keys { get; }

    string? Value { get; }

    IEnumerable<IInferableAttribute> Attributes { get; }

    IReadOnlyCollection<IInferableNode> Children { get; }
}
