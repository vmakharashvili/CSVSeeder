using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CSVSeeder;
public static class SeederExtension
{
    public static void AddSeeder(this DbContext context, string[] args, ILogger logger)
    {
//#if DEBUG
//        args = new string[] { "--seed", "entitydata" };
//#endif
        try
        {
            if (args.Contains("--seed"))
            {
                var cmdINdex = Array.IndexOf(args, "--seed");
                if (args.Length < cmdINdex + 2)
                {
                    throw new ArgumentException("--seed command requires entities");
                }
                var properties = context.GetType().GetProperties();
                var entityList = args[cmdINdex..];
                var entities = properties.Where(x => x.PropertyType.GenericTypeArguments.Any(x => entityList.Contains(x.Name.ToLower()))).ToList();
                if (entities.Count == 0)
                {
                    throw new ArgumentException($"No entities were identified with names: {string.Join(", ", entityList)}");
                }
                CsvHelpers.GenerateSeederMigrationFiles(entities);
                logger.LogWarning("Seeding migration files have been generated for the following entities: {entities}", string.Join(", ", entityList));
                Environment.Exit(0);
                return;
            }

            var connectionString = context.Database.GetConnectionString() ?? throw new ArgumentException("Couldn't get database connection string");
            SqlHelpers.ExecuteSql(@"IF NOT EXISTS (SELECT * FROM sysobjects where Name='__SeedingHistory' AND xtype='U')
                                         CREATE Table [dbo].[__SeedingHistory]([Name] VARCHAR(500) NOT NULL)", connectionString);

            var latestSeederHistoryName = SqlHelpers.GetLatestSeederHistory(connectionString);

            var files = Directory.GetFiles(DirectoryConstants.Migrations);
            var snapshotFiles = Directory.GetFiles(DirectoryConstants.Snapshot).Where(x => !x.EndsWith("txt")).ToArray();
            var snapShotMigrationInfo = File.ReadAllText(DirectoryConstants.SnapshotMigrationInfo);
            var snapshotMigrationFiles = files;
            if (snapShotMigrationInfo != "")
            {
                var snapShotInfoIndex = Array.IndexOf(files, $"{DirectoryConstants.Migrations}\\{snapShotMigrationInfo}");
                snapshotMigrationFiles = snapshotMigrationFiles[(snapShotInfoIndex + 1)..];
            }
            if (latestSeederHistoryName == null && snapShotMigrationInfo != "" && snapshotFiles?.Length > 0)
            {
                SnapshotSynchronization.SynchronizeSnapshot(context, snapshotFiles);
                SqlHelpers.AddNewSeederHistory(connectionString, snapShotMigrationInfo);
                var fileIndex = Array.IndexOf(files, $"{DirectoryConstants.Migrations}\\{snapShotMigrationInfo}");
                files = files[(fileIndex + 1)..];
            }
            if (latestSeederHistoryName != null)
            {
                var fileINdex = Array.IndexOf(files, $"{DirectoryConstants.Migrations}\\{latestSeederHistoryName}");
                files = files[(fileINdex + 1)..];
            }
            if (files.Length > 0)
            {
                CsvHelpers.ApplyMigrations(files, context, connectionString, logger);
            }
        }
        catch (ArgumentException argEx)
        {
            logger.LogError(argEx.Message);
            Environment.Exit(-1);
        }
        catch (Exception ex)
        {
            logger.LogError("Unidentified Exception: {0}", ex);
            Environment.Exit(-1);
        }
    }
}

