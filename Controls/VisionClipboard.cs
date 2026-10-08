using System.Text.Json;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Miche.Mac.Models;
using Miche.Mac.Services;

namespace Miche.Mac.Controls;

public sealed partial class VisionView
{
    private const string VisionClipboardPrefix="miche-vision:v1\n";
    private sealed record VisionCopy(Guid WorkspaceId,Guid MicheId,VisionItem[] Items);
    public string? CopySelection()
    {
        if(_editor is not null||!PrepareToLeave())return null;
        var items=Items().Where(i=>i.DeletedAt is null&&SelectedIds.Contains(i.Id)).Select(WorkspaceStore.CopyVision).ToArray();
        return items.Length==0?null:VisionClipboardPrefix+JsonSerializer.Serialize(new VisionCopy(_session!.Store.Snapshot.Index.RootMicheId,_micheId,items));
    }
    public bool PasteSelection(string text)
    {
        if(_dialogBorder.IsVisible||_editor is not null||text.Length>2_000_000||!text.StartsWith(VisionClipboardPrefix)||!PrepareToLeave())return false;
        try
        {
            var copy=JsonSerializer.Deserialize<VisionCopy>(text[VisionClipboardPrefix.Length..]);if(copy is null||copy.Items is null||copy.Items.Length is <1 or >1000)return false;
            Guid[] ids=Array.Empty<Guid>();
            if(!_session!.Act(()=>ids=_session.Store.PasteVisionItems(_micheId,copy.WorkspaceId,copy.MicheId,copy.Items,_artifactId)))return false;
            _selection.Clear();_selection.UnionWith(ids);_selected=ids.FirstOrDefault();RefreshBoard();_canvas.Focus();return true;
        }
        catch(JsonException){_message.Text="Couldn’t read those Vision objects.";return false;}
    }
    private async void HandleVisionClipboardKey(KeyEventArgs e)
    {
        if(_editor is not null||e.KeyModifiers.HasFlag(KeyModifiers.Shift)||e.KeyModifiers.HasFlag(KeyModifiers.Alt)||
            (e.KeyModifiers&(KeyModifiers.Meta|KeyModifiers.Control))==0||e.Key is not (Key.C or Key.V))return;
        e.Handled=true;var clipboard=Avalonia.Controls.TopLevel.GetTopLevel(this)?.Clipboard;if(clipboard is null)return;
        try
        {
            if(e.Key==Key.C){var text=CopySelection();if(text is not null)await clipboard.SetTextAsync(text);}
            else {var text=await clipboard.TryGetTextAsync();if(text is not null)PasteSelection(text);}
        }
        catch(Exception ex)when(ex is IOException or ArgumentException or InvalidDataException){_message.Text=ex.Message;}
    }
}
