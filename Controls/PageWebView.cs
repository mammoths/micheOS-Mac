using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Miche.Mac.Models;
using Miche.Mac.Services;
namespace Miche.Mac.Controls;
public sealed class PageWebView:NativeControlHost
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate void Callback(IntPtr text);
    [DllImport("libmeili-webview.dylib",CallingConvention=CallingConvention.Cdecl)]private static extern IntPtr page_create(string assets,Callback callback);
    [DllImport("libmeili-webview.dylib",CallingConvention=CallingConvention.Cdecl)]private static extern void page_release(IntPtr view);
    [DllImport("libmeili-webview.dylib",CallingConvention=CallingConvention.Cdecl)]private static extern void page_eval(IntPtr view,string script);
    [DllImport("libmeili-webview.dylib",CallingConvention=CallingConvention.Cdecl)]private static extern void page_flush(IntPtr view,Callback callback);
    private readonly WorkspaceSession _session;private readonly Guid _id;private IntPtr _view;private readonly Callback _receive;private Callback? _flush;private bool _ready;private string _childrenJson="";
    public event Action<string>? Status;
    public PageWebView(WorkspaceSession session,Guid id){_session=session;_id=id;_receive=p=>{var message=Marshal.PtrToStringUTF8(p)??"";if(Dispatcher.UIThread.CheckAccess())Receive(message);else Dispatcher.UIThread.Post(()=>Receive(message));};}
    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {if(!OperatingSystem.IsMacOS()||parent.HandleDescriptor!="NSView")return base.CreateNativeControlCore(parent);_view=page_create(Path.Combine(AppContext.BaseDirectory,"PageEditor"),_receive);return new PlatformHandle(_view,"NSView");}
    protected override void DestroyNativeControlCore(IPlatformHandle control){_ready=false;if(_view!=IntPtr.Zero){page_release(_view);_view=IntPtr.Zero;}else base.DestroyNativeControlCore(control);}
    private static string J(object? o)=>JsonSerializer.Serialize(o);
    public void Evaluate(string script){if(_view!=IntPtr.Zero&&_ready)page_eval(_view,script);}
    public void Rename(string title)=>Evaluate("window.pageSetTitle("+J(title)+")");
    public void ChildrenChanged(){var json=J(Children());if(json==_childrenJson)return;_childrenJson=json;Evaluate("window.pageChildren("+json+")");}
    private object Children()=>_session.Pages.Snapshot.Pages.Where(p=>p.ParentId==_id&&p.DeletedAt is null).Select(p=>new{p.Id,p.Title}).ToArray();
    private void Load(){_ready=true;var p=_session.Pages.Get(_id);var images=new Dictionary<string,string>();foreach(var b in p.Blocks.Where(b=>b.Kind=="image")){try{images[b.FileName!]=_session.Pages.ImageData(b.FileName!);}catch(IOException){Status?.Invoke("An image file is missing; its saved reference is retained.");}}
        Evaluate("window.pageLoad("+J(new{p.Title,p.Blocks})+","+J(images)+","+J(Children())+")");}
    private bool Save(string text)
    {if(text.Length>2000000)throw new InvalidDataException("This page is too large to save.");using var doc=JsonDocument.Parse(text);var p=doc.RootElement;var blocks=p.GetProperty("Blocks").Deserialize<List<PageBlock>>()??throw new InvalidDataException("Invalid page content.");_session.Pages.SaveContent(_id,p.GetProperty("Title").GetString()??"",blocks);return true;}
    private void Receive(string message)
    {
        try{using var doc=JsonDocument.Parse(message);var b=doc.RootElement;switch(b.GetProperty("action").GetString()){
            case "ready":Load();break;
            case "save":Save(b.GetProperty("text").GetString()!);Evaluate("window.pageSaveStatus(true,'saved')");break;
            case "image":var data=Convert.FromBase64String(b.GetProperty("data").GetString()!);var name=_session.Pages.ImportImage(data);Evaluate("window.pageInsertImage("+J(name)+","+J(_session.Pages.ImageData(name))+")");break;
            case "newPage":_session.CreatePage(24,24,_id);break;
            case "openPage":var id=b.GetProperty("id").GetGuid();if(_session.Pages.Get(id).ParentId!=_id)throw new ArgumentException("This page is not in this collection.");_session.PopoutPage(id);break;
            case "deleteChildPage":var child=b.GetProperty("id").GetGuid();if(_session.Pages.Get(child).ParentId!=_id)throw new ArgumentException("This page is not in this collection.");_session.DeletePage(child);break;
            case "quit":_session.TryQuit();break;
            case "error":throw new ArgumentException(b.GetProperty("message").GetString());
        }}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException or InvalidOperationException or FormatException){Status?.Invoke(ex.Message);Evaluate("window.pageSaveStatus(false,"+J(ex.Message)+")");}
    }
    public void Flush(Action<bool> done)
    {
        if(_view==IntPtr.Zero){done(true);return;}if(!_ready){done(false);Status?.Invoke("Page is still loading. Keep it open and retry.");return;}
        if(_flush is not null){done(false);return;}
        _flush=p=>{var text=Marshal.PtrToStringUTF8(p)??"";Dispatcher.UIThread.Post(()=>{_flush=null;try{if(text=="error")throw new IOException("Page could not save; keep it open and retry.");Save(text);done(true);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException or InvalidOperationException){Status?.Invoke(ex.Message);done(false);}});};page_flush(_view,_flush);
    }
}
