using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Reflection;
using System.Text;

namespace CSVSeeder;
internal class SqlHelpers
{
    public static string? CheckTableIdentityColumn(string tableName, string schema, string connectionString)
    {
        return ReadSingleSql<string>($"SELECT name from syscolumns where id = Object_ID('{schema}.{tableName}') and colstat & 1 = 1", connectionString, reader => reader.GetString(0));
    }

    public static void NoCheckConstraints(string tableName, string schema, string connectionString)
    {
        ExecuteSql($"ALTER TABLE {schema}.{tableName} NOCHECK CONSTRAINT ALL", connectionString);
    }

    public static void RecoverCheckConstraints(string tableName, string schema, string connectionString)
    {
        ExecuteSql($"ALTER TABLE {schema}.{tableName} WITH CHECK CHECK CONSTRAINT ALL", connectionString);
    }

    public static void ExecuteSql(string sql, string connectionString)
    {
        using var conn = new SqlConnection(connectionString);
        using var cmd = new SqlCommand(sql, conn);
        conn.Open();
        cmd.ExecuteNonQuery();
        conn.Close();
    }

    public static void ExecuteSqlWithParameters(string sql, string connectionString, params SqlParameter[] sqlParameters)
    {
        using var conn = new SqlConnection(connectionString);
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddRange(sqlParameters);
        conn.Open();
        cmd.ExecuteNonQuery();
        conn.Close();
    }

    public static T? ReadSingleSql<T>(string sql, string connectionString, Func<SqlDataReader, T> readFunc)
    {
        var result = default(T);
        using var conn = new SqlConnection(connectionString);
        using var cmd = new SqlCommand(sql, conn);
        conn.Open();
        var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result = readFunc(reader);

        }
        reader.Close();
        conn.Close();
        return result;
    }

    public static string? GetLatestSeederHistory(string connectionString)
    {
        return ReadSingleSql("SELECT TOP 1 [Name] FROM dbo.__SeedingHistory ORDER BY [Name] DESC", connectionString, r => r.GetString(0));
    }

    public static void AddNewSeederHistory(string connectionString, string value)
    {
        ExecuteSqlWithParameters("INSERT INTO dbo.__SeedingHistory (Name) VALUES (@Name)", connectionString, new SqlParameter { 
            DbType = System.Data.DbType.String, ParameterName = "@Name", Value = value });
    }

    public static void InsertInDb(IEntityType et, string connectionString, IEnumerable<PropertyInfo> entityProperties, object entityInstance)
    {
        var schema = et.GetSchema() ?? throw new ArgumentException("Couldn't get schema");
        var table = et.GetTableName() ?? throw new ArgumentException("Couldn't get table name");
        var primaryKey = et.FindPrimaryKey();
        int j = 0;
        var sql = new StringBuilder();
        var vb = new StringBuilder();
        var tableIdentityColumn = CheckTableIdentityColumn(table, schema, connectionString);
        var identityColumnValue = tableIdentityColumn == null ? null : entityProperties.Where(x => x.Name == tableIdentityColumn).First().GetValue(entityInstance);
        var convertedValue = identityColumnValue == null ? default :
            entityProperties.Where(x => x.Name == tableIdentityColumn).First().PropertyType == typeof(int) ?
            Convert.ToInt32(identityColumnValue) :
            throw new ArgumentException("Autonumbered property type not recognized");
        if (tableIdentityColumn != null && identityColumnValue != null && convertedValue != default)
        {
            sql.Append($"SET IDENTITY_INSERT [{schema}].[{table}] ON \n");
        }
        sql.Append($"INSERT INTO [{schema}].[{table}] WITH (tablock) (");
        var c = 0;
        foreach (var h in entityProperties)
        {
            if (h.Name == tableIdentityColumn && convertedValue == default)
            {
                continue;
            }

            if (c > 0)
            {
                sql.Append(", ");
                vb.Append(", ");
            }
            sql.Append($"[{h.Name}]");
            vb.Append($"@{h.Name}");
            c++;
        }
        sql.Append(") OUTPUT Inserted.ID VALUES (");
        sql.Append(vb);
        sql.Append(") \n");
        if (tableIdentityColumn != null && identityColumnValue != null && convertedValue != default)
        {
            sql.Append($"SET IDENTITY_INSERT [{schema}].[{table}] OFF \n");
        }
        using var sqlConn = new SqlConnection(connectionString);
        using var sqlCmd = new SqlCommand(sql.ToString(), sqlConn);
        j = 0;
        foreach (var p in entityProperties)
        {
            var avlue = p.GetValue(entityInstance) ?? DBNull.Value;
            sqlCmd.Parameters.AddWithValue($"@{p.Name}", avlue);
            j++;
        }
        sqlConn.Open();
        var id = sqlCmd.ExecuteScalar();
        var prkType = primaryKey?.Properties[0];
        if (id != null && id != default && prkType != null)
        {
            entityInstance.GetType().GetProperty(prkType.Name)!.SetValue(entityInstance, Convert.ChangeType(id, prkType.ClrType));
        }
        sqlConn.Close();
    }

    public static object? ReadOldEntity(IEntityType et, string connectionString, IEnumerable<PropertyInfo> entityProperties, TypeInfo entityType, object entityInstance)
    {
        var schema = et.GetSchema() ?? throw new ArgumentException("Couldn't get schema");
        var table = et.GetTableName() ?? throw new ArgumentException("Couldn't get table name");
        var primaryKey = et.FindPrimaryKey();
        var sql = new StringBuilder();
        var tableIdentityColumn = CheckTableIdentityColumn(table, schema, connectionString);
        var identityColumnValue = tableIdentityColumn == null ? null : entityProperties.Where(x => x.Name == tableIdentityColumn).First().GetValue(entityInstance);
        sql.Append("SELECT ");
        var i = 0;
        foreach(var entityProperty in entityProperties)
        {
            if(i > 0)
            {
                sql.Append(", ");
            }
            sql.Append($"{entityProperty.Name}");
            i++;
        }

        sql.Append($" FROM [{schema}].[{table}] WHERE {tableIdentityColumn}={identityColumnValue}");
        var entityPropertiesArray = entityProperties.ToArray();
        return ReadSingleSql(sql.ToString(), connectionString, r =>
        {
            var entity = Activator.CreateInstance(entityType) ?? throw new ArgumentException("Couldn't get EntityIntance");
            for(int i =0; i < entityPropertiesArray.Length; i++)
            {
                entity.GetType().GetProperty(entityPropertiesArray[i].Name)!.SetValue(entity, r[i]);
            }
            return entity;
        });
    }

    public static void UpdateInDb(IEntityType et, string connectionString, IEnumerable<PropertyInfo> entityProperties, object entityInstance)
    {
        var schema = et.GetSchema() ?? throw new ArgumentException("Couldn't get schema");
        var table = et.GetTableName() ?? throw new ArgumentException("Couldn't get table name");
        var primaryKey = et.FindPrimaryKey();
        var sql = new StringBuilder();
        var tableIdentityColumn = CheckTableIdentityColumn(table, schema, connectionString);
        var identityColumnValue = tableIdentityColumn == null ? null : entityProperties.Where(x => x.Name == tableIdentityColumn).First().GetValue(entityInstance);
        sql.Append($"UPDATE [{schema}].[{table}] SET ");
        var sqlParameters = new List<SqlParameter>();
        foreach(var entityProperty in entityProperties.Where(x => x.Name != tableIdentityColumn))
        {
            sql.Append($"{entityProperty.Name}=@{entityProperty.Name}");
            sqlParameters.Add(new SqlParameter { ParameterName = $"@{entityProperty.Name}", DbType = entityProperty.PropertyType.ToSqlDbType(), Value = entityProperty.GetValue(entityInstance)});
        }

        sql.Append($" WHERE {tableIdentityColumn}={identityColumnValue}");
        ExecuteSqlWithParameters(sql.ToString(), connectionString, sqlParameters.ToArray());
    }

    public static void DeleteInDb(IEntityType et, string connectionString, IEnumerable<PropertyInfo> entityProperties, object entityInstance)
    {
        var schema = et.GetSchema() ?? throw new ArgumentException("Couldn't get schema");
        var table = et.GetTableName() ?? throw new ArgumentException("Couldn't get table name");
        var primaryKey = et.FindPrimaryKey() ?? throw new ArgumentException("Couldn't get Primary key");
        var sql = new StringBuilder();
        var primaryKeyColumn = entityProperties.Where(x => x.Name == primaryKey.Properties[0].PropertyInfo?.Name).First();
        var identityColumnValue = primaryKeyColumn.GetValue(entityInstance);
        sql.Append($"DELETE FROM [{schema}].[{table}] WHERE {primaryKeyColumn.Name}=@{primaryKeyColumn.Name}");
        var sqlpars = new SqlParameter() { ParameterName = $"@{primaryKeyColumn.Name}", Value = identityColumnValue, DbType = primaryKeyColumn.PropertyType.ToSqlDbType() };
        ExecuteSqlWithParameters(sql.ToString(), connectionString, sqlpars);
    }
}
