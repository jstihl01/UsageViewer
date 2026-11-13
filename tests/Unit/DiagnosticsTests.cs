using UsageViewer.Core;
using Xunit;

namespace UsageViewer.Core.Tests;

public class DiagnosticsTests
{
    [Fact] public void LogsRotateAndNeverPersistUntrustedText()
    {
        string root=Path.Combine(Path.GetTempPath(),"UsageViewer-"+Guid.NewGuid().ToString("N"));
        try
        {
            var log=new EventLog(root,"1.1.0",500);
            for(int i=0;i<40;i++)
            {
                log.Write("rpc_error","https://auth.openai.com/?token=private-token",
                    new IOException("private@example.invalid password=private-password"),401);
                log.Stderr("private-token private@example.invalid C:\\Users\\PrivateName\\auth.json");
            }
            var files=Directory.GetFiles(root);
            Assert.InRange(files.Length,1,3);
            string text=string.Concat(files.Select(File.ReadAllText));
            Assert.DoesNotContain("private",text,StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("auth.json",text);
            Assert.All(files,path=>Assert.True(new FileInfo(path).Length<=500));
        }
        finally
        {
            if(Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)
                &&Path.GetFileName(root).StartsWith("UsageViewer-")&&Directory.Exists(root))
                Directory.Delete(root,true);
        }
    }
    [Fact] public void UnwritableLogsNeverThrow()
    {
        string path=Path.GetTempFileName();
        try { new EventLog(path,"1.1.0").Write("startup",error:new IOException("secret")); }
        finally { File.Delete(path); }
    }
}
