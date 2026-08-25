#r "nuget: Microsoft.Data.Sqlite, 10.0.11"

using Microsoft.Data.Sqlite;

if (Args.Count != 2)
{
    Console.Error.WriteLine("Usage: dotnet script tools/backup-sqlite.csx -- <source.db> <backup.db>");
    return 2;
}

var sourcePath = Path.GetFullPath(Args[0]);
var backupPath = Path.GetFullPath(Args[1]);

if (!File.Exists(sourcePath))
{
    Console.Error.WriteLine($"Source database does not exist: {sourcePath}");
    return 2;
}

if (File.Exists(backupPath))
{
    Console.Error.WriteLine($"Refusing to overwrite existing backup: {backupPath}");
    return 2;
}

var sourceConnectionString = new SqliteConnectionStringBuilder
{
    DataSource = sourcePath,
    Mode = SqliteOpenMode.ReadOnly
}.ToString();

var backupConnectionString = new SqliteConnectionStringBuilder
{
    DataSource = backupPath,
    Mode = SqliteOpenMode.ReadWriteCreate
}.ToString();

using (var source = new SqliteConnection(sourceConnectionString))
using (var backup = new SqliteConnection(backupConnectionString))
{
    source.Open();
    backup.Open();
    source.BackupDatabase(backup);
}

Console.WriteLine($"SQLite backup created: {backupPath} ({new FileInfo(backupPath).Length} bytes)");
return 0;
