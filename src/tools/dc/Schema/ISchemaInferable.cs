namespace Vezel.Novadrop.Schema;

internal interface ISchemaInferable
{
    internal interface IKeys
    {
        bool HasAttributeNames { get; }

        IEnumerable<string> AttributeNames { get; }
    }

    internal interface IAttribute
    {
        string Key { get; }

        DataCenterTypeCode TypeCode { get; }
    }

    string Name { get; }

    IKeys? Keys { get; }

    string? Value { get; }

    IEnumerable<IAttribute> Attributes { get; }

    IReadOnlyCollection<ISchemaInferable> Children { get; }
}
