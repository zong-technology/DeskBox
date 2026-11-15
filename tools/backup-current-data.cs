// Phase 0 utility: create a real pre-upgrade snapshot of the selected DeskBox data directory.
using System;
using DeskBox;
using DeskBox.Core;

public class BackupCurrentData
{
    public static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: backup-current-data.exe <data-dir> [label]");
            return 2;
        }
        Store.DataDir = args[0];
        AppPaths.DataDir = args[0];
        var label = args.Length > 1 ? args[1] : "pre-upgrade";
        var result = DataBackupService.CreateBackup(label);
        Console.WriteLine("ok=" + result.Ok);
        Console.WriteLine("files=" + result.FileCount);
        Console.WriteLine("directory=" + result.Directory);
        Console.WriteLine("message=" + result.Message);
        return result.Ok ? 0 : 1;
    }
}
