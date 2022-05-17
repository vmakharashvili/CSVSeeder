using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic.FileIO;
using System.Reflection;
using System.Text;
using System.Transactions;

namespace CSVSeeder;
internal class CsvHelpers
{
    public static Dictionary<string, string?[]> LoadSnapshot(string key)
    {
        var parser = new TextFieldParser(key) { HasFieldsEnclosedInQuotes = true };
        parser.SetDelimiters(";");
        var i = 0;
        var result = new Dictionary<string, string?[]>();
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields() ?? throw new ArgumentException("Couldn't read fields from csv file");
            if (i == 0)
            {
                result.Add("Headeers", fields);
                i++;
                continue;
            }

            result.Add(fields[0], fields);

            i++;
        }
        parser.Close();
        return result;
    }

    public static KeyValuePair<string, string?[]>? UpdateSnapshot(Dictionary<string, Dictionary<string, string?[]>> fileSnapshot, string key, IEnumerable<PropertyInfo> entityProperties, object entityInstance, bool remove)
    {
        int i = 0;
        string? valueKey = null;
        string?[] values = new string[entityProperties.Count()];
        KeyValuePair<string, string?[]>? oldValue = null;
        foreach (var entityProperty in entityProperties)
        {
            if (i == 0)
            {
                valueKey = entityProperty.GetValue(entityInstance)?.ToString();
            }

            values[i] = entityProperty.GetValue(entityInstance)?.ToString();

            i++;
        }
        if (valueKey == null)
        {
            throw new ArgumentException("Couldn't parse value key");
        }

        if (fileSnapshot[key].ContainsKey(valueKey))
        {
            oldValue = new KeyValuePair<string, string?[]>(valueKey, fileSnapshot[key][valueKey]);
            fileSnapshot[key][valueKey] = values;
            if (remove)
            {
                fileSnapshot[key].Remove(valueKey);
            }
        }
        else
        {
            fileSnapshot[key].Add(valueKey, values);
        }

        return oldValue;
    }

    public static void GenerateSeederMigrationFiles(List<PropertyInfo> entities)
    {
        foreach (var entity in entities)
        {
            var entityType = (TypeInfo)entity.PropertyType.GenericTypeArguments[0];
            var pp = entityType.DeclaredProperties.FilterDbProperties();
            var sb = new StringBuilder();
            foreach (var h in pp)
            {
                sb.Append(h.Name);
                sb.Append(';');
            }
            sb.Append("SeedCommand;");
            sb.Append('\n');
            if (!Directory.Exists(DirectoryConstants.Seeder))
            {
                Directory.CreateDirectory(DirectoryConstants.Seeder);
            }

            if (!Directory.Exists(DirectoryConstants.Migrations))
            {
                Directory.CreateDirectory(DirectoryConstants.Migrations);
            }

            if (!Directory.Exists(DirectoryConstants.Snapshot))
            {
                Directory.CreateDirectory(DirectoryConstants.Snapshot);
            }

            if (!File.Exists(DirectoryConstants.SnapshotMigrationInfo))
            {
                File.Create(DirectoryConstants.SnapshotMigrationInfo);
            }

            if (!File.Exists($"{DirectoryConstants.Snapshot}/{entityType.Name}.csv"))
            {
                File.AppendAllText($"{DirectoryConstants.Snapshot}/{entityType.Name}.csv", sb.ToString());
            }

            var seedFileName = $"{DirectoryConstants.Migrations}/{DateTime.Now:yyyyMMddHHmmss}_{entityType.Name}_Seed.csv";
            File.AppendAllText(seedFileName, sb.ToString());
        }
    }

    public static void ApplyMigrations(string[] files, DbContext context, string connectionString, ILogger logger)
    {
        Dictionary<string, List<string>> snapshots = new();
        Dictionary<string, Dictionary<string, List<string?>>> previousVersionAdditions = new();
        Dictionary<string, Dictionary<string, string?[]>> fileSnapshots = new();
        string? lastMigrationfile = null;
        using var transaction = new TransactionScope();
        List<(string schema, string table)> constraintChecks = new();
        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            var fileNameSplit = fileName.Split('_');
            var contextProperties = context.GetType().GetProperties();
            var entityH = contextProperties.Where(x => x.PropertyType.GenericTypeArguments.Any(x => x.Name.ToLower() == fileNameSplit[1].ToLower())).ToList();
            var entityType2 = (TypeInfo)entityH[0].PropertyType.GenericTypeArguments[0];
            var entityProperties = entityType2.DeclaredProperties.FilterDbProperties();
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

            var fileSnapshotKey = $"{DirectoryConstants.Snapshot}/{fileNameSplit[1]}.csv";
            fileSnapshots.Add(fileSnapshotKey, CsvHelpers.LoadSnapshot(fileSnapshotKey) ?? throw new ArgumentException("Couldn't load snapshot"));

            var parser = new TextFieldParser(file)
            {
                HasFieldsEnclosedInQuotes = true
            };
            parser.SetDelimiters(";");

            string[]? line;
            var fileLine = 0;
            while (!parser.EndOfData)
            {
                line = parser.ReadFields();
                if(line?[0] == "Previous Version Info:")
                {
                    break;
                }
                if (line?.Length > 0)
                {
                    logger.LogInformation("Reading Seeder Migration file: {line}", String.Join(";", line));
                    if (fileLine > 0)
                    {
                        var entityPropertiesArray = entityProperties.ToArray();
                        var migrationCmd = MigrationCmd.Create;
                        for (int i = 0; i <= entityPropertiesArray.Length; i++)
                        {
                            if (i == entityPropertiesArray.Length)
                            {
                                migrationCmd = line[i].Trim().ToUpper() switch
                                {
                                    "C" => MigrationCmd.Create,
                                    "U" => MigrationCmd.Update,
                                    "D" => MigrationCmd.Delete,
                                    _ => throw new ArgumentException("Migration Command not recognized")
                                };
                                continue;
                            }
                            if (line[i] != string.Empty)
                            {
                                if (entityPropertiesArray[i].PropertyType == typeof(Guid))
                                {
                                    entityInstance!.GetType().GetProperty(entityPropertiesArray[i].Name)!.SetValue(entityInstance, Guid.Parse(line[i]));
                                }
                                else if (entityPropertiesArray[i].PropertyType == typeof(DateTime))
                                {
                                    entityInstance!.GetType().GetProperty(entityPropertiesArray[i].Name)!
                                        .SetValue(entityInstance, line[i] == "" ? default : Convert.ToDateTime(line[i]));
                                }
                                else if (entityPropertiesArray[i].PropertyType == typeof(DateTime?))
                                {
                                    entityInstance!.GetType().GetProperty(entityPropertiesArray[i].Name)!
                                        .SetValue(entityInstance, line[i] == "" ? null : Convert.ToDateTime(line[i]));
                                }
                                else
                                {
                                    entityInstance!.GetType().GetProperty(entityPropertiesArray[i].Name)!
                                        .SetValue(entityInstance, Convert.ChangeType(line[i], entityPropertiesArray[i].PropertyType));
                                }
                            }
                        }
                        KeyValuePair<string, string?[]>? oldValue = null;
                        switch (migrationCmd)
                        {
                            case MigrationCmd.Create:
                                SqlHelpers.InsertInDb(et, connectionString, entityProperties, entityInstance);
                                CsvHelpers.UpdateSnapshot(fileSnapshots, fileSnapshotKey, entityProperties, entityInstance, false);
                                break;
                            case MigrationCmd.Update:
                                oldValue = CsvHelpers.UpdateSnapshot(fileSnapshots, fileSnapshotKey, entityProperties, entityInstance, false);
                                SqlHelpers.UpdateInDb(et, connectionString, entityProperties, entityInstance);
                                break;
                            case MigrationCmd.Delete:
                                oldValue = CsvHelpers.UpdateSnapshot(fileSnapshots, fileSnapshotKey, entityProperties, entityInstance, true);
                                SqlHelpers.DeleteInDb(et, connectionString, entityProperties, entityInstance);
                                break;
                        }

                        if (oldValue != null)
                        {
                            if (!previousVersionAdditions.ContainsKey(fileName))
                            {
                                previousVersionAdditions.Add(fileName, new Dictionary<string, List<string?>> {
                                        { "Headers", entityProperties.Select(x => (string?)x.Name).ToList() }
                                    });
                            }

                            previousVersionAdditions[fileName].Add(oldValue.Value.Key, oldValue.Value.Value.ToList());
                        }
                        lastMigrationfile = fileName;
                    }
                    fileLine++;
                }
                entityInstance = Activator.CreateInstance(entityType2) ?? throw new ArgumentException("Couldn't get EntityIntance");
            }
            parser.Close();
            SqlHelpers.AddNewSeederHistory(connectionString, fileName);
            logger.LogWarning("Following Seeder Migration Applied => {migration}", fileName);
        }

        foreach (var (schema, table) in constraintChecks)
        {
            SqlHelpers.RecoverCheckConstraints(table, schema, connectionString);
        }
        constraintChecks = new();
        transaction.Complete();


        if (previousVersionAdditions.Count > 0)
        {
            foreach (var previoiusVersionAddition in previousVersionAdditions)
            {
                using var writer = new StreamWriter($"{DirectoryConstants.Migrations}/{previoiusVersionAddition.Key}", true);
                writer.WriteLine();
                writer.WriteLine("Previous Version Info:");
                foreach (var fields in previoiusVersionAddition.Value)
                {
                    var sb = new StringBuilder();
                    foreach (var field in fields.Value)
                    {
                        if (field?.Contains(";") == true)
                        {
                            sb.Append("\"" + field + "\";");
                        }
                        else
                        {
                            sb.Append($"{field};");
                        }
                    }
                    writer.WriteLine(sb.ToString());
                }
            }
        }

        if (fileSnapshots.Count > 0)
        {
            foreach (var fileSnapshot in fileSnapshots)
            {
                using var writer = new StreamWriter(fileSnapshot.Key, false);
                foreach (var fields in fileSnapshot.Value)
                {
                    var sb = new StringBuilder();
                    var myfields = fields.Value;
                    if (fields.Value.Contains("SeedCommand"))
                    {
                        var seedCommandIndex = Array.IndexOf(fields.Value, "SeedCommand");
                        myfields = fields.Value[0..seedCommandIndex];
                    }
                    foreach (var field in myfields)
                    {
                        if (field?.Contains(';') == true)
                        {
                            sb.Append("\"" + field + "\";");
                        }
                        else
                        {
                            sb.Append($"{field};");
                        }
                    }
                    writer.WriteLine(sb.ToString());
                }
            }
            using var writer2 = new StreamWriter(DirectoryConstants.SnapshotMigrationInfo, false);
            writer2.Write(lastMigrationfile);
            lastMigrationfile = null;
        }
    }
}
