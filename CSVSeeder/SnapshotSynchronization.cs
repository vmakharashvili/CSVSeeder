using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;
using System.Text;
using System.Transactions;

namespace CSVSeeder;

internal class SnapshotSynchronization
{
    public static void SynchronizeSnapshot(DbContext context, string[] snapshotFiles)
    {
        var connectionString = context.Database.GetConnectionString() ?? throw new ArgumentNullException(nameof(context), "Coultn't get connectionstring");
        using var transaction = new TransactionScope();
        List<(string schema, string table)> constraintChecks = new();
        foreach (var file in snapshotFiles)
        {
            var fileName = Path.GetFileName(file).Split('.');
            var properties = context.GetType().GetProperties();
            var entityH = properties.Where(x => x.PropertyType.GenericTypeArguments.Any(x => x.Name.ToLower() == fileName[0].ToLower())).ToList();
            var entityType2 = (System.Reflection.TypeInfo)entityH[0].PropertyType.GenericTypeArguments[0];
            var hh = entityType2.DeclaredProperties.Where(x => x.GetGetMethod()?.IsVirtual == false);
            var entityInstance = Activator.CreateInstance(entityType2) ?? throw new ArgumentException("Couldn't get EntityIntance");
            var et = context.Model.FindEntityType(entityType2) ?? throw new ArgumentException("Couldn't get Entity Type");
            var schema = et.GetSchema() ?? throw new ArgumentException("Couldn't get schema");
            var table = et.GetTableName() ?? throw new ArgumentException("Couldn't get table name");
            var primaryKey = et.FindPrimaryKey();
            if (!constraintChecks.Any(x => x.schema == schema && x.table == table))
            {
                constraintChecks.Add((schema, table));
                SqlHelpers.NoCheckConstraints(table, schema, connectionString);
            }

            var parser = new TextFieldParser(file)
            {
                HasFieldsEnclosedInQuotes = true
            };
            parser.SetDelimiters(";");

            string[]? line;
            var i = 0;
            while (!parser.EndOfData)
            {
                line = parser.ReadFields();
                if (line?.Length > 0)
                {
                    if (i > 0)
                    {
                        var j = 0;
                        foreach (var h in hh)
                        {
                            if (line[j] != string.Empty)
                            {
                                if (h.PropertyType == typeof(Guid))
                                {
                                    entityInstance!.GetType().GetProperty(h.Name)!.SetValue(entityInstance, Guid.Parse(line[j]));
                                }
                                else if (h.PropertyType == typeof(DateTime) || h.PropertyType == typeof(DateTime?))
                                {
                                    entityInstance!.GetType().GetProperty(h.Name)!.SetValue(entityInstance, line[j] == "" ? default : Convert.ToDateTime(line[j]));
                                }
                                else
                                {
                                    entityInstance!.GetType().GetProperty(h.Name)!.SetValue(entityInstance, Convert.ChangeType(line[j], h.PropertyType));
                                }
                            }
                            j++;
                        }
                        j = 0;
                        var sql = new StringBuilder();
                        var vb = new StringBuilder();
                        var pp = ((System.Reflection.TypeInfo)entityInstance.GetType()).DeclaredProperties.Where(x => x.GetGetMethod()?.IsVirtual == false);
                        var tableIdentityColumn = SqlHelpers.CheckTableIdentityColumn(table, schema, connectionString);
                        var identityColumnValue = tableIdentityColumn == null ? null : pp.Where(x => x.Name == tableIdentityColumn).First().GetValue(entityInstance);
                        var convertedValue = identityColumnValue == null ? default :
                            pp.Where(x => x.Name == tableIdentityColumn).First().PropertyType == typeof(int) ?
                            Convert.ToInt32(identityColumnValue) :
                            throw new ArgumentException("Autonumbered property type not recognized");
                        if (tableIdentityColumn != null && identityColumnValue != null && convertedValue != default)
                        {
                            sql.Append($"SET IDENTITY_INSERT [{schema}].[{table}] ON \n");
                        }
                        sql.Append($"INSERT INTO [{schema}].[{table}] WITH (tablock) (");
                        var c = 0;
                        foreach (var h in hh)
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
                        foreach (var p in pp)
                        {
                            //if(p.Name == tableIdentityColumn && convertedValue == default)
                            //{
                            //    continue;
                            //}
                            sqlCmd.Parameters.AddWithValue($"@{p.Name}", line[j]);
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
                        j = 0;
                    }
                    i++;
                }
                entityInstance = Activator.CreateInstance(entityType2) ?? throw new ArgumentException("Couldn't get EntityIntance");
            }
        }
        foreach (var (schema, table) in constraintChecks)
        {
            SqlHelpers.RecoverCheckConstraints(table, schema, connectionString);
        }
        constraintChecks = new();

        transaction.Complete();
    }
}
