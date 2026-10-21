using Buildup.Core;

if (args.Length == 1 && args[0] is "--help" or "-h")
{
    Console.WriteLine("Usage: buildup [--dry-run]");
    Console.WriteLine("Deletes ignored files only in Git repositories rooted at or below the current directory.");
    Console.WriteLine("--dry-run  List what would be deleted without changing files.");
    return 0;
}

if (args.Length > 1 || (args.Length == 1 && args[0] != "--dry-run"))
{
    Console.Error.WriteLine("Usage: buildup [--dry-run]");
    return 2;
}

try
{
    var result = new ArtifactCleaner().Clean(Directory.GetCurrentDirectory(), args.Length == 1,
        entry => Console.WriteLine(entry.Path + (entry.IsDirectory ? Path.DirectorySeparatorChar : "")));
    foreach (var error in result.Errors)
        Console.Error.WriteLine(error);
    return result.Errors.Count == 0 ? 0 : 1;
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
    or InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
