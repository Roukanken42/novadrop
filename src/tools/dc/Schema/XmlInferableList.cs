namespace Vezel.Novadrop.Schema;

internal partial class XmlInferableList : IInferableNode
{
    internal class XmlInferableElement : IInferableNode
    {
        private readonly XElement _element;

        public XmlInferableElement(XElement element)
        {
            _element = element;
        }

        public string Name => _element.Name.LocalName;

        public IInferableNode.IInferableKeys? Keys => null;

        public string? Value => _element.Nodes().FirstOrDefault(a => a is XText)?.ToString();

        public IEnumerable<IInferableNode.IInferableAttribute> Attributes => _element.Attributes().Select(attr => new XmlInferableAttribute(attr));

        public IReadOnlyCollection<IInferableNode> Children => _element.Elements().Select(child => new XmlInferableElement(child)).ToList();
    }

    public partial class XmlInferableAttribute : IInferableNode.IInferableAttribute
    {
        private readonly XAttribute _attribute;

        public XmlInferableAttribute(XAttribute attribute)
        {
            _attribute = attribute;
        }

        public string Key => _attribute.Name.LocalName;

        public DataCenterTypeCode TypeCode => InferType(_attribute.Value);

        private static DataCenterTypeCode InferType(string value)
        {
            return value switch
            {
                "true" or "false" => DataCenterTypeCode.Boolean,
                var x when IntRegex().IsMatch(x) && int.TryParse(x, out _) => DataCenterTypeCode.Int32,
                var x when SingleRegex().IsMatch(x) && float.TryParse(x, out _) => DataCenterTypeCode.Single,
                _ => DataCenterTypeCode.String,
            };
        }

        [GeneratedRegex("[0-9]+")]
        private static partial Regex IntRegex();

        [GeneratedRegex("[0-9]+(\\.[0-9]+)?")]
        private static partial Regex SingleRegex();
    }

    private readonly List<XElement> _nodes;

    public XmlInferableList(List<XElement> nodes)
    {
        _nodes = nodes;
    }

    public string Name => "__root__";

    public IInferableNode.IInferableKeys? Keys => null;

    public string? Value => null;

    public IEnumerable<IInferableNode.IInferableAttribute> Attributes => Enumerable.Empty<IInferableNode.IInferableAttribute>();

    public IReadOnlyCollection<IInferableNode> Children => _nodes.Select(child => new XmlInferableElement(child)).ToList();
}
