using System.Data;
using Dapper;

namespace TelegramGroupsAdmin.Data.Infrastructure;

/// <summary>
/// Dapper type handler to convert SQLite INTEGER (0/1) to C# bool
/// </summary>
public class SqliteBooleanHandler : SqlMapper.TypeHandler<bool>
{
    public override bool Parse(object value)
    {
        return value switch
        {
            long l => l != 0,
            int i => i != 0,
            bool b => b,
            _ => false
        };
    }

    public override void SetValue(IDbDataParameter parameter, bool value)
    {
        parameter.Value = value ? 1 : 0;
    }
}
