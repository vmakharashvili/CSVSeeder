# CSVSeeder
Seeder Library for EntityFrameworkCore

#Idea
Data seeding in EntityFramework is very symple, we just need to hardcode data classes. This has some issues: 1) If seed data is large, it increases code-base; 2) When we issue new versions and need to add some seed-data in new version, we have to add more code, which is quite cumbersome to search old versions of data (It can be seen only in git history); 3) Hardcoded data requires to write whole classes with all property names, which is wordy; 4) If seed data is really big, starting project becomes slow, because EntityFramework has to check that all hardcoded seed data is present in database.

**CSVSeeder** is solving this problem with migrations (idea is taken from EfCore migrations). The library creates migrations, snapshot and seeder-migration history in database. Migrations register changes in seed data, Snapshot is for empty database to apply first time to avoid applying all migrations. Snapshot helps also to remove old migration files. CSVSeeder uses csv files, because they are very skinny and aren't included in application code-base. Also, library saves migration steps and only those migrations are applied which database has not applied (On version update on Test, Staging, Production and other environments).

## Install the library

With cli we need to run this command:

> `dotnet add package CSVSeeder`

In Startup file (Or in Program file in .net 6.0) in applicationBuilder part, we need to get Existed EfCore DbContext from ServiceProvider, logger and apply this extension method from CSVSeeder library:

> `context.AddSeeder(args, logger);`

That's all. Here we pass Program Main(string[] artgs) parameters.

---

## Use the library

When everything is set-up and we know which entity we need to seed, go to the application start-up project root and open terminal. In console we write following code:

> `dotnet run --seed entity1 entity2...

Here we write down those entity names we want to seed. Entity names are not case-sensitive. This creates Seed folder in root project, which has 2 sub-folders: **Migrations** and **Snapshot**. 

- **Migrations** folder is for automatically generated seed-migration files. As many migration files will be created as many entities we point in the command above. If we write down 3 entities, three migration files will be created. 
- **Snapshot** folder holds snapshot csv files by entity and SnapshotMigrationInfo.text file. One file per Entity is created here. SnapshotMIgrationInfo.text holds last migration file name, which is applied to the snapshot files. When the above command is run, migration files are applied, but they are not applied into snapshot yet.

Next stage is to fill migration files. With file explorer, we open Migrations folder and open newly generated migration files and enter data there. Here we have 3 type of SeederCommands we must point:

> **C** - Create, **U** - Update, **D** - Delete. 

In migration files we can create new record, update existed one and delete it for new version if desired. When filling it, we save the files.

At last stage we just run the project and it applies the migration. It updates database and snapshot at the same time. If we move database to other environment where seeding head is behind, it checks Seeding migration history table there and applies migrations there as well (Only in database).
In migration when we have Update and Delete operations, after applying the migration, in the migration files we have previous record version records. This is good for history tracking.
