using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CSVSeeder;
public class DirectoryConstants
{
    public const string Seeder = "Seeder";
    public const string Migrations = $"{Seeder}/Migrations";
    public const string Snapshot = $"{Seeder}/Snapshot";
    public const string SnapshotMigrationInfo = $"{Snapshot}/__SnapshotMigrationInfo.txt";
}
