namespace Vezel.Novadrop.Schema;

internal class DataCenterSchemaInferable : ISchemaInferable
{
    internal class DataCenterKeys : ISchemaInferable.IKeys
    {
        private readonly Data.DataCenterKeys _keys;

        public DataCenterKeys(Data.DataCenterKeys keys)
        {
            _keys = keys;
        }

        public bool HasAttributeNames => _keys.HasAttributeNames;

        public IEnumerable<string> AttributeNames => _keys.AttributeNames;
    }

    public class DataCenterAttribute : ISchemaInferable.IAttribute
    {
        private readonly DataCenterValue _value;

        public DataCenterAttribute(string key, DataCenterValue value)
        {
            Key = key;
            _value = value;
        }

        public string Key { get; }

        public DataCenterTypeCode TypeCode => _value.TypeCode;
    }

    private readonly DataCenterNode _node;

    public DataCenterSchemaInferable(DataCenterNode node)
    {
        _node = node;
    }

    public string Name => _node.Name;

    public ISchemaInferable.IKeys? Keys => new DataCenterKeys(_node.Keys);

    public string? Value => _node.Value;

    public IEnumerable<ISchemaInferable.IAttribute> Attributes => _node.Attributes.Select(a => new DataCenterAttribute(a.Key, a.Value));

    public IReadOnlyCollection<ISchemaInferable> Children => _node.Children.Select(child => new DataCenterSchemaInferable(child)).ToList();
}
