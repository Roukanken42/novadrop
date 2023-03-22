namespace Vezel.Novadrop.Schema;

internal sealed class DataCenterAttributeSchema
{
    public DataCenterTypeCode TypeCode { get; set; }

    public bool IsOptional { get; set; }

    public DataCenterAttributeSchema(DataCenterTypeCode typeCode)
    {
        TypeCode = typeCode;
    }
}
