namespace Vezel.Novadrop.Data.Nodes;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
abstract class ImmutableDataCenterNode : DataCenterNode
{
    public override sealed string? Value
    {
        get => base.Value;
        set => throw new NotSupportedException();
    }

    public override sealed DataCenterKeys Keys
    {
        get => base.Keys;
        set => throw new NotSupportedException();
    }

    public override sealed bool IsImmutable => true;

    public ImmutableDataCenterNode(object parent, string name, string? value, DataCenterKeys keys)
        : base(parent, name, value, keys)
    {
    }

    public override sealed DataCenterNode CreateChild(string name)
    {
        throw new NotSupportedException();
    }

    public override sealed bool RemoveChild(DataCenterNode node)
    {
        throw new NotSupportedException();
    }

    public override sealed void ClearChildren()
    {
        throw new NotSupportedException();
    }

    public override sealed void AddAttribute(string name, DataCenterValue value)
    {
        throw new NotSupportedException();
    }

    public override sealed void SetAttribute(string name, DataCenterValue value)
    {
        throw new NotSupportedException();
    }

    public override sealed bool RemoveAttribute(string name)
    {
        throw new NotSupportedException();
    }

    public override sealed void ClearAttributes()
    {
        throw new NotSupportedException();
    }
}