namespace Vezel.Novadrop.Schema;

internal class DataCenterInferableNode : IInferableNode
{
    internal class DataCenterInferableKeys : IInferableNode.IInferableKeys
    {
        private readonly DataCenterKeys _keys;

        public DataCenterInferableKeys(DataCenterKeys keys)
        {
            _keys = keys;
        }

        public bool HasAttributeNames => _keys.HasAttributeNames;

        public IEnumerable<string> AttributeNames => _keys.AttributeNames;
    }

    public class DataCenterInferableAttribute : IInferableNode.IInferableAttribute
    {
        private readonly DataCenterValue _value;

        public DataCenterInferableAttribute(string key, DataCenterValue value)
        {
            Key = key;
            _value = value;
        }

        public string Key { get; }

        public DataCenterTypeCode TypeCode => _value.TypeCode;
    }

    private readonly DataCenterNode _node;

    public DataCenterInferableNode(DataCenterNode node)
    {
        _node = node;
    }

    public string Name => _node.Name;

    public IInferableNode.IInferableKeys? Keys => new DataCenterInferableKeys(_node.Keys);

    public string? Value => _node.Value;

    public IEnumerable<IInferableNode.IInferableAttribute> Attributes => _node.Attributes.Select(a => new DataCenterInferableAttribute(a.Key, a.Value));

    public IReadOnlyCollection<IInferableNode> Children => _node.Children.Select(child => new DataCenterInferableNode(child)).ToList();
}
