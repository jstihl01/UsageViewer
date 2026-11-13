using System.Diagnostics;
using UsageViewer.Windows;

if(args.Contains("--parent"))
{
    using var job=new ProcessJob();
    using var child=Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
    { ArgumentList={"--child"},UseShellExecute=false,CreateNoWindow=true })!;
    job.Assign(child);
    Console.WriteLine(child.Id);
    await Console.Out.FlushAsync();
    await Task.Delay(Timeout.Infinite);
}
else await Task.Delay(Timeout.Infinite);
