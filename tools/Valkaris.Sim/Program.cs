using System.Diagnostics;
using System.Globalization;
using Valkaris.Core.Data;
using Valkaris.Sim;

// Reports are compared between runs, so they must not follow the machine's locale.
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var options = SimOptions.Parse(args);
var data = GameData.Load(new FolderDataSource(options.DataPath ?? FolderDataSource.FindDataFolder()));

if (options.EfficiencyTable)
{
    Console.Write(CombatEfficiency.Table(data));
    return 0;
}

var watch = Stopwatch.StartNew();
var (results, failures) = MatchRunner.RunAll(options, data);
watch.Stop();

var report = SimReport.Build(results, failures);
Console.Write(report.ToTable());
Console.WriteLine($"Took {watch.Elapsed.TotalSeconds:0.0}s");

if (options.OutPath is { } path)
    File.WriteAllText(path, report.ToJson());

return failures.Count == 0 ? 0 : 1;

namespace Valkaris.Sim
{
    /// <summary>Reads data files from a folder on disk.</summary>
    public sealed class FolderDataSource : IDataSource
    {
        private readonly string _folder;

        public FolderDataSource(string folder) => _folder = folder;

        public string ReadText(string fileName) => File.ReadAllText(Path.Combine(_folder, fileName));

        /// <summary>The data folder of the repository this is running from.</summary>
        public static string FindDataFolder()
        {
            var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Valkaris.sln")))
                directory = directory.Parent;
            return directory is null
                ? throw new DirectoryNotFoundException("Run from inside the repository, or pass --data=folder.")
                : Path.Combine(directory.FullName, "data");
        }
    }
}
