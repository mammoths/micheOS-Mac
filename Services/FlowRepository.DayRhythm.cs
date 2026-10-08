// Timing and planning semantics adapted from current Windows Flow.
using System.Text.Json;
using Miche.Mac.Models;

namespace Miche.Mac.Services;

public static class DailyRhythm
{
    // Wake and bed inherit independently. A later explicit choice is a new
    // change point; editing an earlier date never replaces that later choice.
    public static TodayPreferences ForDate(TodayDocument doc,DateOnly date)
    {
        var prefs=JsonSerializer.Deserialize<TodayPreferences>(JsonSerializer.Serialize(doc.Preferences))!;
        var key=date.ToString("yyyy-MM-dd");
        foreach(var pair in doc.DayRhythms.Where(p=>string.CompareOrdinal(p.Key,key)<=0).OrderBy(p=>p.Key,StringComparer.Ordinal))
        {
            if(pair.Value.Wake is {} wake)prefs.MorningStart=wake;
            if(pair.Value.Bed is {} bed)prefs.WindDownTime=bed;
        }
        return prefs;
    }
}

public sealed partial class FlowRepository
{
    public void SetDayRhythm(DateOnly date,bool wake,TimeOnly? time)=>Change(d=>{
        var key=date.ToString("yyyy-MM-dd");
        if(!d.DayRhythms.TryGetValue(key,out var rhythm))d.DayRhythms[key]=rhythm=new();
        if(wake)rhythm.Wake=time;else rhythm.Bed=time;
        if(rhythm.Wake==null&&rhythm.Bed==null)d.DayRhythms.Remove(key);
    });
    private static void ValidateDayRhythms(TodayDocument d)
    {
        Require(d.DayRhythms!=null,"Unreadable daily times.");
        foreach(var pair in d.DayRhythms!)
        {
            Require(DateOnly.TryParseExact(pair.Key,"yyyy-MM-dd",out var day)&&pair.Value!=null,"Invalid daily time entry.");
            var prefs=DailyRhythm.ForDate(d,day);
            Require(prefs.MorningStart!=prefs.WindDownTime,"Wake-up and bedtime need different times.");
        }
    }
}
