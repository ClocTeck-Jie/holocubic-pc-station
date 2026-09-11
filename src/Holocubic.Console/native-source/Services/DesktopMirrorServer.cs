using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Net.WebSockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Clocteck.CubicCenter.Services;

public sealed record MirrorOptions(int Monitor=1,string Region="",string Fit="stretch",int Fps=8,int Quality=65);
public sealed class DesktopMirrorServer : IAsyncDisposable {
    private WebApplication? server;
    private CancellationTokenSource? stop;
    private Task? loop;
    private readonly ConcurrentDictionary<Guid,WebSocket> clients=new();
    private MirrorOptions options=new();
    private readonly Action<string> log;
    public DesktopMirrorServer(Action<string> log) {this.log=log;}
    public bool Running=>server!=null;
    public MirrorOptions Options=>options;
    public static object[] Screens()=>System.Windows.Forms.Screen.AllScreens.Select((s,i)=>(object)new {index=i+1,name=s.DeviceName,x=s.Bounds.X,y=s.Bounds.Y,width=s.Bounds.Width,height=s.Bounds.Height,primary=s.Primary}).ToArray();
    public void Configure(MirrorOptions value){
        if(value.Monitor<1||value.Monitor>System.Windows.Forms.Screen.AllScreens.Length||value.Fps<1||value.Fps>30||value.Quality<1||value.Quality>95||!new[]{"stretch","contain","crop"}.Contains(value.Fit))throw new ArgumentException("投屏参数不正确");
        SourceBounds(value); options=value;
    }
    private static Rectangle SourceBounds(MirrorOptions value){
        if(string.IsNullOrWhiteSpace(value.Region))return System.Windows.Forms.Screen.AllScreens[Math.Clamp(value.Monitor-1,0,System.Windows.Forms.Screen.AllScreens.Length-1)].Bounds;
        var parts=value.Region.Split(',');
        if(parts.Length!=4||parts.Any(p=>!int.TryParse(p,out _)))throw new ArgumentException("区域格式：x,y,宽,高");
        var n=parts.Select(int.Parse).ToArray();if(n[2]<1||n[3]<1)throw new ArgumentException("区域大小不正确");
        var region=Rectangle.Intersect(new Rectangle(n[0],n[1],n[2],n[3]),System.Windows.Forms.SystemInformation.VirtualScreen);
        if(region.Width<1||region.Height<1)throw new ArgumentException("区域不在显示器范围内");return region;
    }
    public async Task Start(){
        if(server!=null)return;var builder=WebApplication.CreateBuilder(new WebApplicationOptions{Args=[],ContentRootPath=AppContext.BaseDirectory});builder.Logging.ClearProviders();builder.WebHost.ConfigureKestrel(s=>s.ListenAnyIP(8787));
        var app=builder.Build();app.UseWebSockets();app.MapGet("/health",()=>Results.Json(new{ok=true,service="desktop-mirror",clients=clients.Count}));
        app.MapGet("/{**path}",async (HttpContext ctx)=>{
            if(!ctx.WebSockets.IsWebSocketRequest){await ctx.Response.WriteAsJsonAsync(new{service="desktop-mirror",format="DMJ1",width=320,height=240,clients=clients.Count,settings=options});return;}
            using var socket=await ctx.WebSockets.AcceptWebSocketAsync();var id=Guid.NewGuid();clients[id]=socket;log("投屏设备已连接");
            try{var buffer=new byte[1024];while(socket.State==WebSocketState.Open&&!ctx.RequestAborted.IsCancellationRequested){var message=await socket.ReceiveAsync(buffer,ctx.RequestAborted);if(message.MessageType==WebSocketMessageType.Close)break;}}catch(OperationCanceledException){}catch(WebSocketException){}finally{clients.TryRemove(id,out _);socket.Abort();}
        });
        try{await app.StartAsync();server=app;stop=new();loop=Task.Run(()=>CaptureLoop(stop.Token));}catch{await app.DisposeAsync();throw;}
    }
    private async Task CaptureLoop(CancellationToken token){
        uint frame=0;
        while(!token.IsCancellationRequested){var start=Stopwatch.StartNew();var settings=options;
            try{if(!clients.IsEmpty){var payload=Capture(++frame,settings,clients.Count);await Task.WhenAll(clients.Select(async pair=>{try{using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(1000);await pair.Value.SendAsync(payload,WebSocketMessageType.Binary,true,timeout.Token);}catch{if(clients.TryRemove(pair.Key,out var socket))socket.Abort();}}));}}
            catch(Exception e) when(e is not OperationCanceledException){log("投屏采集失败："+e.Message);await Task.Delay(1500,token);}
            try{await Task.Delay(Math.Max(1,(int)(1000d/settings.Fps-start.ElapsedMilliseconds)),token);}catch(OperationCanceledException){break;}
        }
    }
    private static byte[] Capture(uint frame,MirrorOptions options,int clientCount){
        var timer=Stopwatch.StartNew();var rect=SourceBounds(options);
        if(options.Fit=="crop"){var aspect=4d/3;if(rect.Width/(double)rect.Height>aspect){var w=(int)(rect.Height*aspect);rect.X+=(rect.Width-w)/2;rect.Width=w;}else{var h=(int)(rect.Width/aspect);rect.Y+=(rect.Height-h)/2;rect.Height=h;}}
        using var source=new Bitmap(rect.Width,rect.Height,PixelFormat.Format24bppRgb);using(var g=Graphics.FromImage(source))g.CopyFromScreen(rect.Location,Point.Empty,rect.Size,CopyPixelOperation.SourceCopy);
        ushort captureMs=(ushort)Math.Min(timer.ElapsedMilliseconds,65535);timer.Restart();
        using var image=new Bitmap(320,240,PixelFormat.Format24bppRgb);using(var g=Graphics.FromImage(image)){g.Clear(Color.Black);g.InterpolationMode=InterpolationMode.HighQualityBilinear;var target=new Rectangle(0,0,320,240);if(options.Fit=="contain"){var scale=Math.Min(320d/rect.Width,240d/rect.Height);var w=(int)(rect.Width*scale);var h=(int)(rect.Height*scale);target=new Rectangle((320-w)/2,(240-h)/2,w,h);}g.DrawImage(source,target);}
        ushort resizeMs=(ushort)Math.Min(timer.ElapsedMilliseconds,65535);timer.Restart();using var jpeg=new MemoryStream();using var ep=new EncoderParameters(1);ep.Param[0]=new EncoderParameter(System.Drawing.Imaging.Encoder.Quality,(long)options.Quality);image.Save(jpeg,ImageCodecInfo.GetImageEncoders().First(c=>c.MimeType=="image/jpeg"),ep);
        ushort encodeMs=(ushort)Math.Min(timer.ElapsedMilliseconds,65535);using var output=new MemoryStream();using var writer=new BinaryWriter(output);writer.Write("DMJ1"u8);writer.Write((byte)1);writer.Write((byte)0);writer.Write((ushort)36);writer.Write(frame);writer.Write((uint)jpeg.Length);writer.Write((ushort)320);writer.Write((ushort)240);writer.Write((ushort)options.Quality);writer.Write((ushort)(options.Fps*100));writer.Write(captureMs);writer.Write(resizeMs);writer.Write(encodeMs);writer.Write((ushort)(captureMs+resizeMs+encodeMs));writer.Write((ushort)0);writer.Write((ushort)clientCount);writer.Write(jpeg.ToArray());return output.ToArray();
    }
    public async Task Stop(){if(server==null)return;stop?.Cancel();foreach(var socket in clients.Values)socket.Abort();clients.Clear();if(loop!=null){try{await loop;}catch(OperationCanceledException){}}await server.StopAsync();await server.DisposeAsync();server=null;stop?.Dispose();stop=null;}
    public async ValueTask DisposeAsync()=>await Stop();
}
