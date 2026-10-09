using Miche.Mac.Services;

namespace Miche.Mac.Controls;

internal sealed record MicheColor(string Name,string Note,string Top,string Bottom,string Surface);

internal static class MichePalette
{
    public static readonly MicheColor[] Colors = {
        new("rose", "#DABBB6", "#422D36", "#251A22", "#34242E"),
        new("sage", "#CED8BF", "#344030", "#1B251D", "#283326"),
        new("blue", "#BCCEDC", "#2D3B4B", "#192332", "#233043"),
        new("lilac", "#D6C4DD", "#3B2E48", "#211B30", "#30253C"),
        new("honey", "#E8D5AA", "#483A27", "#2B2118", "#372D21"),
        new("sea glass", "#BFD8D1", "#2C4440", "#182B28", "#233732")
    };
    public static MicheColor For(Models.Miche miche)=>Colors.Single(c=>c.Note==WorkspaceStore.MicheNoteColor(miche));
}
