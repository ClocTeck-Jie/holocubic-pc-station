using System.Text;
using System.Text.Json;
using System.IO.Compression;
using System.Diagnostics;
using Microsoft.Win32;
using Clocteck.CubicCenter.Services;
using Clocteck.CubicCenter.Core;
using Clocteck.CubicCenter.Models;

if(ElevatedSensorHost.IsHostCommand(args)){await ElevatedSensorHost.RunAsync(args);return;}

Console.InputEncoding = new UTF8Encoding(false);
Console.OutputEncoding = new UTF8Encoding(false);
var outputLock = new object();
void Send(object value) { lock(outputLock) Console.WriteLine(JsonSerializer.Serialize(value)); }
if (args.Length == 2 && args[0] == "--apply-update") {
    var job = JsonDocument.Parse(File.ReadAllText(args[1])).RootElement;
    var current = Path.GetFullPath(job.GetProperty("current").GetString()!);
    var stage = Path.GetFullPath(job.GetProperty("stage").GetString()!);
    var backup = current + ".previous";
    var exe = Path.GetFullPath(job.GetProperty("exe").GetString()!);
    try {
        if(Path.GetFileName(current)!="app" || Path.GetDirectoryName(current)!=Path.GetDirectoryName(stage) || !Path.GetFileName(stage).StartsWith("app.next-") || Path.GetDirectoryName(exe)!=Path.GetDirectoryName(Path.GetDirectoryName(current))) throw new Exception("Invalid update destination");
        try { if(!Process.GetProcessById(job.GetProperty("pid").GetInt32()).WaitForExit(30000))throw new Exception("等待旧版本退出超时"); } catch(ArgumentException) {}
        if(job.TryGetProperty("children",out var children))foreach(var child in children.EnumerateArray()){try{if(!Process.GetProcessById(child.GetInt32()).WaitForExit(15000))throw new Exception("等待后台服务退出超时");}catch(ArgumentException){}}
        if(Directory.Exists(backup)) Directory.Move(backup, backup + "." + DateTime.UtcNow.Ticks);
        Directory.Move(current,backup);
        try { Directory.Move(stage,current); } catch { Directory.Move(backup,current); throw; }
        Process.Start(new ProcessStartInfo(exe){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(exe)!});
    } catch(Exception ex) { File.WriteAllText(args[1]+".error.txt",ex.ToString()); }
    return;
}
WindowsSerialConnection? connection = null;
BuiltinApiServer? builtins = null;
var appLog=new AppLog();
appLog.EntryAdded+=(_,e)=>Send(new{type="service-log",text=e.Source+"："+e.Message});
BuiltinApiServer Builtins()=>builtins??=new BuiltinApiServer(appLog,new PerformanceMonitorSettings{EnableElevatedSensorHost=false});
await using var mirror=new DesktopMirrorServer(message=>Send(new{type="service-log",text=message}));
var decoder = new UTF8Encoding(false,false).GetDecoder();
while(await Console.In.ReadLineAsync() is string line) {
    int id=0;
    try {
        var doc=JsonDocument.Parse(line).RootElement; id=doc.GetProperty("id").GetInt32();
        var command=doc.GetProperty("command").GetString();
        var data=doc.TryGetProperty("data",out var d)?d:default;
        object result=new { };
        if(command=="service-status") {result=new[]{"pc-monitor","holopet","codex-core"}.Select(id=>new{id,running=builtins?.IsRunning(id)==true,message=builtins?.GetStatusMessage(id)??"已停止"}).Append(new{id="desktop-mirror",running=mirror.Running,message=mirror.Running?"运行中":"已停止"}).ToArray();}
        else if(command=="service-start") {var service=data.GetProperty("service").GetString()!;if(service=="desktop-mirror")await mirror.Start();else{await Builtins().StartAsync(service);if(!Builtins().IsRunning(service))throw new Exception(Builtins().GetStatusMessage(service));}result=new{running=true};}
        else if(command=="service-stop") {var service=data.GetProperty("service").GetString()!;if(service=="desktop-mirror")await mirror.Stop();else if(builtins!=null)await builtins.StopAsync(service);result=new{running=false};}
        else if(command=="monitor-snapshot") result=Builtins().GetMonitorSnapshot();
        else if(command=="monitor-config") {
            var bindings=data.TryGetProperty("bindings",out var map)?JsonSerializer.Deserialize<Dictionary<string,string>>(map.GetRawText()):null;
            Builtins().ConfigureMonitorBindings(bindings);
            result=data.TryGetProperty("initial",out var initial)&&initial.ValueKind==JsonValueKind.True?new{configured=true}:Builtins().GetMonitorSnapshot();
        }
        else if(command=="monitor-elevate") result=await Builtins().StartElevatedSensorHostAsync();
        else if(command=="mirror-status") result=new{screens=DesktopMirrorServer.Screens(),settings=mirror.Options};
        else if(command=="mirror-config") {var o=JsonSerializer.Deserialize<MirrorOptions>(data.GetRawText(),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;mirror.Configure(o);result=mirror.Options;}
        else if(command=="ports") {
            using var key=Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
            result=key?.GetValueNames().Select(n=>key.GetValue(n)?.ToString()).Where(n=>n!=null).Distinct().OrderBy(n=>n).ToArray()??[];
        } else if(command=="open") {
            var port=data.GetProperty("port").GetString()!;var baud=data.GetProperty("baud").GetInt32();
            if(!System.Text.RegularExpressions.Regex.IsMatch(port,@"^COM[1-9][0-9]{0,3}$")||baud<1200||baud>3000000)throw new Exception("串口参数不正确");
            if(connection?.IsOpen==true&&connection.PortName==port&&connection.BaudRate==baud){Send(new{id,ok=true,value=new{port,baud}});continue;}
            connection?.Dispose();connection=new WindowsSerialConnection(port,baud);decoder.Reset();
            connection.BytesReceived+=(_,e)=>{var chars=new char[e.Bytes.Length*2];var count=decoder.GetChars(e.Bytes,chars,false);Send(new{type="serial",text=new string(chars,0,count)});};
            connection.ErrorReceived+=(_,e)=>Send(new{type="serial-error",text=e.Error.Message});
            connection.Start();result=new{port,baud};
        } else if(command=="close") {connection?.Dispose();connection=null;}
        else if(command=="write") {if(connection?.IsOpen!=true)throw new Exception("串口未连接");connection.Write(Encoding.UTF8.GetBytes(data.GetProperty("text").GetString()??""));}
        else if(command=="stage-update") {
            var zipPath=data.GetProperty("path").GetString()!;var current=Path.GetFullPath(data.GetProperty("current").GetString()!);
            using var zip=ZipFile.OpenRead(zipPath);
            if(zip.Entries.Count>10000||zip.Entries.Sum(e=>e.Length)>300_000_000)throw new Exception("更新包过大");
            var manifest=zip.GetEntry("app/package.json")??throw new Exception("更新包缺少 app/package.json");
            using var reader=new StreamReader(manifest.Open());var pkg=JsonDocument.Parse(reader.ReadToEnd()).RootElement;
            if(pkg.GetProperty("name").GetString()!="holocubic-console")throw new Exception("这不是 holocubic 控制台更新包");
            var version=pkg.GetProperty("version").GetString()!;
            if(!Version.TryParse(version,out var next)||next<=Version.Parse(data.GetProperty("version").GetString()!))throw new Exception("更新包版本必须高于当前版本");
            var stage=Path.Combine(Path.GetDirectoryName(current)!,"app.next-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            try {
                foreach(var entry in zip.Entries){
                    if(!entry.FullName.StartsWith("app/",StringComparison.Ordinal))throw new Exception("更新包只能包含 app 目录");
                    var rel=entry.FullName[4..];if(rel.Length==0)continue;
                    var target=Path.GetFullPath(Path.Combine(stage,rel));
                    if(!target.StartsWith(stage+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||rel.Contains(':')||(entry.ExternalAttributes>>16&0xf000)==0xa000)throw new Exception("更新包包含无效路径");
                    if(entry.FullName.EndsWith('/')){Directory.CreateDirectory(target);continue;}
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);entry.ExtractToFile(target,false);
                }
                foreach(var required in new[]{"main.cjs","preload.cjs","index.html","app.js","native/HoloNative.exe"})if(!File.Exists(Path.Combine(stage,required)))throw new Exception("更新包不完整："+required);
            } catch {Directory.Delete(stage,true);throw;}
            result=new{stage,version};
        } else throw new Exception("Unknown command");
        Send(new{id,ok=true,value=result});
    }catch(Exception ex){Send(new{id,ok=false,error=ex.Message});}
}
connection?.Dispose();
if(builtins!=null)await builtins.DisposeAsync();
