using System.Data;

namespace CSVSeeder;
internal static class Typeconvertor
{
    public static DbType ToSqlDbType(this Type t)
    {
        return Type.GetTypeCode(t) switch
        {
            TypeCode.Int32 => DbType.Int32,
            TypeCode.Boolean => DbType.Boolean,
            TypeCode.DateTime => DbType.DateTime,
            TypeCode.String => DbType.String,
            TypeCode.Int64 => DbType.Int64,
            TypeCode.SByte => DbType.SByte,
            TypeCode.Byte => DbType.Byte,
            TypeCode.Char => DbType.String,
            TypeCode.Double => DbType.Double,
            TypeCode.Decimal => DbType.Decimal,
            TypeCode.Int16 => DbType.Int16,
            TypeCode.Object => DbType.Object,
            _ => throw new NotSupportedException("Data type not recognized")
        };
    }
}
