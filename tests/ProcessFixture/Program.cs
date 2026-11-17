using System.Diagnostics;
using UsageViewer.Windows;
using System.Text.Json;

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
else if(args.Contains("--listen"))
{
    while(await Console.In.ReadLineAsync() is {} line)
    {
        using var message=JsonDocument.Parse(line);var root=message.RootElement;
        if(!root.TryGetProperty("id",out var id))continue;
        string method=root.GetProperty("method").GetString()!;
        object result=new{};
        if(method=="test/hang")continue;
        if(method=="test/environment")result=new
        {
            processId=Environment.ProcessId,profile=Environment.GetEnvironmentVariable("CODEX_HOME"),
            hasCodexKey=Environment.GetEnvironmentVariable("CODEX_API_KEY") is not null,
            hasOpenAiKey=Environment.GetEnvironmentVariable("OPENAI_API_KEY") is not null,
            arguments=args
        };
        if(method=="test/spawn")
        {
            using var child=Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
            {ArgumentList={"--child"},UseShellExecute=false,CreateNoWindow=true})!;
            result=new{processId=child.Id};
        }
        Console.WriteLine(JsonSerializer.Serialize(new{id=id.Clone(),result}));
        await Console.Out.FlushAsync();
    }
}
else await Task.Delay(Timeout.Infinite);
