namespace Vezel.Novadrop.Schema;

internal partial class XmlSchemaInferable : ISchemaInferable
{
    internal class XmlSchemaInferableElement : ISchemaInferable
    {
        private readonly XElement _element;

        public XmlSchemaInferableElement(XElement element)
        {
            _element = element;
        }

        public string Name => _element.Name.LocalName;

        public ISchemaInferable.IKeys? Keys => null;

        public string? Value => _element.Nodes().FirstOrDefault(a => a is XText)?.ToString();

        public IEnumerable<ISchemaInferable.IAttribute> Attributes => _element.Attributes().Select(attr => new XmlAttribute(attr));

        public IReadOnlyCollection<ISchemaInferable> Children => _element.Elements().Select(child => new XmlSchemaInferableElement(child)).ToList();
    }

    public partial class XmlAttribute : ISchemaInferable.IAttribute
    {
        private readonly XAttribute _attribute;

        public XmlAttribute(XAttribute attribute)
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
                var v when IntRegex().IsMatch(v) && int.TryParse(v, out _) => DataCenterTypeCode.Int32,
                var v when SingleRegex().IsMatch(v) && float.TryParse(v, out _) => DataCenterTypeCode.Single,
                _ => DataCenterTypeCode.String,
            };
        }

        [GeneratedRegex("[0-9]+")]
        private static partial Regex IntRegex();

        [GeneratedRegex("[0-9]+(\\.[0-9]+)?")]
        private static partial Regex SingleRegex();
    }

    private readonly List<XElement> _nodes;

    public XmlSchemaInferable(List<XElement> nodes)
    {
        _nodes = nodes;
    }

    public string Name => "__root__";

    public ISchemaInferable.IKeys? Keys => null;

    public string? Value => null;

    public IEnumerable<ISchemaInferable.IAttribute> Attributes => Enumerable.Empty<ISchemaInferable.IAttribute>();

    public IReadOnlyCollection<ISchemaInferable> Children => _nodes.Select(child => new XmlSchemaInferableElement(child)).ToList();
}
