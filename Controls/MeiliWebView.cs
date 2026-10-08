using System.Runtime.InteropServices;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Miche.Mac.Controls;

public sealed class MeiliWebView : NativeControlHost
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Callback(IntPtr text);
    [DllImport("libmeili-webview.dylib", CallingConvention=CallingConvention.Cdecl)] private static extern IntPtr meili_create(string assets,string directory,string startUrl,Callback callback);
    [DllImport("libmeili-webview.dylib", CallingConvention=CallingConvention.Cdecl)] private static extern void meili_release(IntPtr view);
    [DllImport("libmeili-webview.dylib", CallingConvention=CallingConvention.Cdecl)] private static extern void meili_flush(IntPtr view,Callback callback);
    private IntPtr _view;
    private Process? _production;
    private readonly Callback _status;
    private Callback? _flush;
    public string DataDirectory { get; }
    public event Action<string>? StatusChanged;
    public MeiliWebView(string directory)
    {
        DataDirectory=Path.Combine(directory,"meili-studio");
        _status=p=>{var text=Marshal.PtrToStringUTF8(p)??"";Dispatcher.UIThread.Post(()=>StatusChanged?.Invoke(text));};
    }
    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!OperatingSystem.IsMacOS() || parent.HandleDescriptor!="NSView") return base.CreateNativeControlCore(parent);
        var assets=Path.Combine(AppContext.BaseDirectory,"MeiliStudio");
        string startUrl="";
        try {
            var info=new ProcessStartInfo("/usr/bin/python3") {RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true};
            info.ArgumentList.Add(Path.Combine(assets,"production.py"));info.ArgumentList.Add("--directory");info.ArgumentList.Add(DataDirectory);info.ArgumentList.Add("--assets");info.ArgumentList.Add(assets);
            _production=Process.Start(info);
            var ready=_production!.StandardOutput.ReadLineAsync();
            if(ready.Wait(TimeSpan.FromSeconds(8)))startUrl=ready.Result??"";
            if(!startUrl.StartsWith("http://127.0.0.1:"))throw new InvalidOperationException("Production service could not start");
        } catch { if(_production is {HasExited:false})_production.Kill();_production=null;StatusChanged?.Invoke("Production service unavailable; original planner opened."); }
        _view=meili_create(assets,DataDirectory,startUrl,_status);
        if(_view==IntPtr.Zero) return base.CreateNativeControlCore(parent);
        return new PlatformHandle(_view,"NSView");
    }
    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if(_production is {HasExited:false})_production.Kill();_production?.Dispose();_production=null;
        if(_view!=IntPtr.Zero) {meili_release(_view);_view=IntPtr.Zero;} else base.DestroyNativeControlCore(control);
    }
    public void Flush(Action<bool,string> done)
    {
        if(_view==IntPtr.Zero) {done(true,"");return;}
        _flush=p=>{var text=Marshal.PtrToStringUTF8(p)??"";Dispatcher.UIThread.Post(()=>{done(text=="ok",text);_flush=null;});};
        meili_flush(_view,_flush);
    }
}
