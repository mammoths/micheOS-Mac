using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using Miche.Mac;
using Miche.Mac.Controls;
using Miche.Mac.Services;

var root = Path.Combine(Path.GetTempPath(), "miche-ui-checks-" + Guid.NewGuid().ToString("N"));
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
int checks = 0;
void Check(bool pass, string why) { if (!pass) throw new Exception(why); checks++; }
void Jobs() => Dispatcher.UIThread.RunJobs();
T Named<T>(Control owner, string name) where T : Control => owner.FindControl<T>(name)!;
void Press(Control owner, string name)
{ Named<Button>(owner, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Jobs(); }
void Click(MainWindow window, string text)
{
    var popup = Named<Avalonia.Controls.Primitives.Popup>(window, "LookupSuggestions");
    if (text == "+ add Dump") { Named<TextBox>(window, "QueryBox").Focus(); Jobs(); }
    var buttons = window.GetVisualDescendants().OfType<Button>().Concat(popup.Child?.GetVisualDescendants().OfType<Button>() ?? Enumerable.Empty<Button>());
    var button = buttons.FirstOrDefault(b => b.Content is string value && value == text && b.IsVisible && !b.Classes.Contains("window-control"));
    if (button is not null) button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    else
    {
        var owner = Named<Button>(window, "SpaceMenuButton");
        owner.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Jobs();
        var item = owner.ContextMenu!.Items.OfType<MenuItem>().Single(i => i.Header as string == text);
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    }
    Jobs();
}
void Render(Window view, string name)
{
    var target = Environment.GetEnvironmentVariable("MICHE_RENDER_DIR");
    if (string.IsNullOrWhiteSpace(target)) return;
    Directory.CreateDirectory(target); Jobs(); view.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Jobs();
    using var bitmap = view.CaptureRenderedFrame();
    bitmap?.Save(Path.Combine(target, name + ".png"));
}
WorkspaceSession? session = null;
try
{
    session = new WorkspaceSession(root);
    var meiliHome=session.OpenHome(); Jobs();
    Check(meiliHome.OpenCommands(), "Meili command search could not open");
    Named<TextBox>(meiliHome,"CommandBox").Text="/thedailymeili"; Jobs();
    Check(Named<Button>(meiliHome,"CommandMeiliSuggestion").IsVisible, "Meili command was not discoverable");
    Named<TextBox>(meiliHome,"CommandBox").RaiseEvent(new KeyEventArgs {RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter}); Jobs();
    var meili=session.MeiliWindow;
    Check(meili?.IsVisible==true, "Meili slash command did not open widget");
    session!.OpenMeili(); Jobs(); Check(ReferenceEquals(meili,session.MeiliWindow), "Meili command duplicated window");
    var full=meili!.GetVisualDescendants().OfType<Button>().Single(b=>b.Content as string=="full size");
    full.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Jobs(); Check(meili.WindowState==WindowState.FullScreen,"Meili full size failed");
    full.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Jobs(); Check(meili.WindowState==WindowState.Normal,"Meili restore failed");
    meili.CloseSaved(); Jobs(); Check(session.MeiliWindow is null,"Meili window did not release");

    var window = session.OpenHome(); Jobs();
    var homeId = session.Store.Snapshot.Index.RootMicheId;
    Check(Named<TextBlock>(window, "SpaceTitle").Text == "home" && Named<StackPanel>(window, "EmptyBoard").IsVisible,
        "First window isn't blank home");
    Render(window, "blank-home");
    window.Width = 480; Jobs();
    Check(Named<Wordmark>(window, "FullWordmark").Bounds.Width <= Named<Grid>(window, "Board").Bounds.Width,
        "Wordmark clips at minimum window width");
    Render(window, "blank-home-narrow"); window.Width = 980; Jobs();
    Click(window, "+ new");
    Check(Named<Border>(window, "NamingPanel").IsVisible && Named<TextBox>(window, "NameBox").IsFocused, "Naming focus failed");
    Render(window, "naming"); window.KeyTextInput("studio");
    Named<TextBox>(window, "NameBox").RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); Jobs();
    Check(Named<TextBlock>(window, "SpaceTitle").Text == "studio", "Create via Enter failed");
    var studioId = session.Store.Snapshot.Index.ActiveMicheId;
    Named<TextBox>(window, "QueryBox").Focus(); window.KeyTextInput("/dump");
    Named<TextBox>(window, "QueryBox").RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); Jobs();
    var dump = (DumpView)Named<ContentControl>(window, "DumpHost").Content!;
    Check(dump is not null && dump.Editor.IsFocused, "Add Dump or editor focus failed");
    window.KeyTextInput("synthetic capture through actual controls");
    dump!.Editor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); Jobs();
    Check(string.IsNullOrEmpty(dump.Editor.Text) && session.Store.Snapshot.RootDump.Single().Text == "synthetic capture through actual controls", "Capture failed");
    Render(window, "dump");
    Click(window, "hide"); Check(Named<StackPanel>(window, "EmptyBoard").IsVisible, "Hide failed");
    Click(window, "+ add Dump"); Click(window, "×"); Click(window, "manage · recently deleted");
    Check(Named<StackPanel>(window, "TrashList").GetVisualDescendants().OfType<Button>().Count(b => b.Content as string == "restore") == 1, "Capture not recoverable");
    Click(window, "restore"); Click(window, "done");
    Click(window, "manage · recently deleted"); Click(window, "rename this niche");
    Named<TextBox>(window, "NameBox").Text = "work"; Click(window, "enter ↵");
    Check(Named<TextBlock>(window, "SpaceTitle").Text == "work", "Rename button failed");

    // Real Flow controls, pointer routing and session lifetime use isolated synthetic data.
    var flowRoot=Path.Combine(root,"flow-ui");
    using(var fs=new WorkspaceSession(flowRoot))
    {
        var home=fs.OpenHome();Jobs();var origin=fs.Store.Snapshot.Index.RootMicheId;
        fs.Store.Create("flow invoker");Jobs();var invoker=fs.Store.Snapshot.Index.ActiveMicheId;
        Press(home,"FlowSummonButton");var fw=fs.FlowWindow!;var fv=fw.View;Jobs();
        Check(fw.IsVisible&&fs.Flow!.OriginMicheId==origin&&fs.Store.Snapshot.Index.ActiveMicheId==invoker,"Flow rebound origin or active Miche");
        Check(fs.OpenFlow()&&ReferenceEquals(fs.FlowWindow,fw),"Summoning Flow created another window");
        fv.CaptureBox.Focus();fw.KeyTextInput("first flow task");fw.KeyPressQwerty(PhysicalKey.Enter,RawInputModifiers.None);fw.KeyReleaseQwerty(PhysicalKey.Enter,RawInputModifiers.None);Jobs();
        fv.CaptureBox.Focus();fw.KeyTextInput("second flow task");fw.KeyPressQwerty(PhysicalKey.Enter,RawInputModifiers.None);fw.KeyReleaseQwerty(PhysicalKey.Enter,RawInputModifiers.None);Jobs();
        var first=fs.Flow!.Snapshot().StackItems[0].Id;var second=fs.Flow.Snapshot().StackItems[1].Id;
        Check(fs.Flow.Snapshot().StackItems.Count==2&&fv.CaptureBox.Text==""&&fs.Flow.Snapshot().StackItems.All(i=>i.StartedAt==null),"Flow capture started timer or lost text");
        fv.SelectTask(first);Jobs();Check(fs.Flow.Snapshot().StackItems.All(i=>i.StartedAt==null),"Flow selection started timer");
        fw.KeyPressQwerty(PhysicalKey.Space,RawInputModifiers.None);fw.KeyReleaseQwerty(PhysicalKey.Space,RawInputModifiers.None);Jobs();
        Check(fs.Flow.Snapshot().StackItems.Single(i=>i.Id==first).StartedAt!=null,"Space did not explicitly start selected task");
        fv.SelectTask(second);fw.KeyPressQwerty(PhysicalKey.Enter,RawInputModifiers.None);fw.KeyReleaseQwerty(PhysicalKey.Enter,RawInputModifiers.None);Jobs();
        Check(fs.Flow.Snapshot().StackItems.Single(i=>i.Id==second).FinishedAt!=null&&fs.Flow.Snapshot().StackItems.Single(i=>i.Id==first).StartedAt!=null,"Enter finished live task instead of selected task");
        fv.SelectTask(first);fw.KeyPressQwerty(PhysicalKey.Backspace,RawInputModifiers.None);Jobs();
        fw.KeyPressQwerty(PhysicalKey.Backspace,RawInputModifiers.None);Jobs();
        Check(fs.Flow.Snapshot().StackItems.Any(i=>i.Id==first)&&fs.Flow.Snapshot().StackItems.Single(i=>i.Id==first).StartedAt==null,"Held Backspace deleted just-reset task");
        fw.KeyReleaseQwerty(PhysicalKey.Backspace,RawInputModifiers.None);Jobs();
        fw.KeyPressQwerty(PhysicalKey.Backspace,RawInputModifiers.None);fw.KeyReleaseQwerty(PhysicalKey.Backspace,RawInputModifiers.None);Jobs();
        Check(!fs.Flow.Snapshot().StackItems.Any(i=>i.Id==first),"Fresh Backspace did not delete selected task");
        fw.KeyPressQwerty(PhysicalKey.Z,RawInputModifiers.Meta);fw.KeyReleaseQwerty(PhysicalKey.Z,RawInputModifiers.Meta);Jobs();
        Check(fs.Flow.Snapshot().StackItems.Any(i=>i.Id==first),"Flow CmdZ did not restore deletion");
        fv.CaptureBox.Text="today pending";Check(fv.SelectCouldDo(),"Could do navigation failed");
        fv.CaptureBox.Text="possibility pending";Check(fv.SelectDay(new DateOnly(2026,10,7)),"Planning navigation failed");
        fv.CaptureBox.Text="future pending";Check(fv.SelectDay(null),"Today navigation failed");
        Check(fv.CaptureBox.Text=="today pending"&&fs.Flow.Snapshot().CouldDoDraft=="possibility pending"&&fs.Flow.Snapshot().StackDayDrafts["2026-10-07"]=="future pending","Flow scopes mixed or lost drafts");
        fs.Flow.Change(d=>d.StackItems.Clear());var a=fs.Flow.AddToStack("drag first");var b=fs.Flow.AddToStack("drag second");var c=fs.Flow.AddToStack("drag third");fv.SaveDraft();fv.SelectTask(a);Jobs();
        Point RowPoint(Guid id)=>fv.RowControls[id].TranslatePoint(new Point(25,16),fw)!.Value;
        var from=RowPoint(a);var to=fv.RowControls[c].TranslatePoint(new Point(25,fv.RowControls[c].Bounds.Height-3),fw)!.Value;
        var rev=fs.Flow.Snapshot().Revision;
        fw.MouseDown(from,MouseButton.Left);fw.MouseUp(to,MouseButton.Left);Jobs();
        Check(fs.Flow.Snapshot().StackItems.Select(i=>i.Id).SequenceEqual(new[]{b,c,a})&&fs.Flow.Snapshot().Revision==rev+1,"Final-release drag did not persist one actual-row reorder: "+string.Join(",",fs.Flow.Snapshot().StackItems.Select(i=>i.Title))+" rev="+fs.Flow.Snapshot().Revision+" before="+rev+" from="+from+" to="+to);
        Check(fv.SelectedId==a&&fs.Flow.Snapshot().StackItems.All(i=>i.StartedAt==null),"Flow drop started timer or changed selection");
        from=RowPoint(a);rev=fs.Flow.Snapshot().Revision;
        fw.MouseDown(from,MouseButton.Left);fw.MouseMove(from+new Vector(0,-100));fw.KeyPressQwerty(PhysicalKey.Escape,RawInputModifiers.None);fw.KeyReleaseQwerty(PhysicalKey.Escape,RawInputModifiers.None);fw.MouseUp(from+new Vector(0,-100),MouseButton.Left);Jobs();
        Check(fs.Flow.Snapshot().Revision==rev&&fs.Flow.Snapshot().StackItems.Last().Id==a,"Cancelled Flow drag persisted");
        fv.SelectTask(b);Jobs();
        var grip=fv.RowControls[b].GetVisualDescendants().OfType<Border>().Single(x=>x.Focusable);
        var at=grip.TranslatePoint(new Point(grip.Bounds.Width/2,grip.Bounds.Height-5),fw)!.Value;rev=fs.Flow.Snapshot().Revision;
        fw.MouseDown(at,MouseButton.Left);fw.MouseMove(at+new Vector(0,30));Jobs();
        Check(fs.Flow.Snapshot().Revision==rev,"Duration preview wrote to journal");
        fw.MouseUp(at+new Vector(0,30),MouseButton.Left);Jobs();
        Check(fs.Flow.Snapshot().StackItems.Single(i=>i.Id==b).Minutes==25&&fs.Flow.Snapshot().Revision==rev+1&&fs.Flow.Snapshot().StackItems.All(i=>i.StartedAt==null),"Duration release lost minutes, wrote twice or started timer");
        // Reserved tasks do not translate; simple click can still select them.
        fs.Flow.Change(d=>d.StackItems.Single(i=>i.Id==a).FixedTime=new TimeOnly(23,0));Jobs();
        from=RowPoint(a);var reserved=fv.RowControls[a];rev=fs.Flow.Snapshot().Revision;
        fw.MouseDown(from,MouseButton.Left);fw.MouseMove(from+new Vector(0,-70));Jobs();
        Check(reserved.RenderTransform is null,"Fixed reservation translated during drag");
        fw.MouseUp(from+new Vector(0,-70),MouseButton.Left);Jobs();
        Check(fs.Flow.Snapshot().Revision==rev&&fs.Flow.Snapshot().StackItems.Single(i=>i.Id==a).FixedTime==new TimeOnly(23,0)&&fs.Flow.Snapshot().StackItems.All(i=>i.StartedAt==null),"Fixed drag changed timing or started timer");
        // The real rhythm dialog writes only changed fields, retaining inheritance.
        void FlowClick(string label){fv.GetVisualDescendants().OfType<Button>().Single(x=>x.Content as string==label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Jobs();}
        TextBox FlowField(string name)=>fv.GetVisualDescendants().OfType<TextBox>().Single(x=>Avalonia.Automation.AutomationProperties.GetName(x)==name);
        var day=new DateOnly(2026,10,7);fv.SelectDay(day);fs.Flow.Change(d=>d.DayRhythms.Clear());fs.Flow.SetDayRhythm(day.AddDays(-1),false,new TimeOnly(22,0));
        FlowClick("rhythm");FlowField("Wake time").Text="8:00 AM";FlowClick("save");
        Check(fs.Flow.Snapshot().DayRhythms["2026-10-07"].Wake==new TimeOnly(8,0)&&fs.Flow.Snapshot().DayRhythms["2026-10-07"].Bed is null,"Wake-only dialog made unwanted bed change point");
        fs.Flow.SetDayRhythm(day.AddDays(-1),false,new TimeOnly(21,0));
        Check(DailyRhythm.ForDate(fs.Flow.Snapshot(),day).WindDownTime==new TimeOnly(21,0),"Wake-only edit blocked inherited earlier bedtime update");
        fs.Flow.Change(d=>d.DayRhythms.Clear());fs.Flow.SetDayRhythm(day.AddDays(-1),true,new TimeOnly(6,0));
        FlowClick("rhythm");FlowField("Bedtime").Text="10:00 PM";FlowClick("save");
        Check(fs.Flow.Snapshot().DayRhythms["2026-10-07"].Wake is null&&fs.Flow.Snapshot().DayRhythms["2026-10-07"].Bed==new TimeOnly(22,0),"Bed-only dialog made unwanted wake change point");
        fs.Flow.SetDayRhythm(day.AddDays(-1),true,new TimeOnly(8,0));
        Check(DailyRhythm.ForDate(fs.Flow.Snapshot(),day).MorningStart==new TimeOnly(8,0),"Bed-only edit blocked inherited earlier wake update");
        fs.Flow.Change(d=>d.DayRhythms.Clear());FlowClick("rhythm");FlowField("Optional treat").Text="a little reward";FlowClick("save");
        Check(fs.Flow.Snapshot().DayRhythms.Count==0&&fs.Flow.Snapshot().Treat=="a little reward","Treat-only save added wake/bed overrides");
        // Task Undo preserves newer input in all scopes and the live capture editor.
        fv.SelectDay(null);fs.Flow.SetStackDuration(b,30);fv.CaptureBox.Text="newer today";fv.SelectCouldDo();fv.CaptureBox.Text="newer possibility";
        fv.SelectDay(day);fv.CaptureBox.Text="newer tomorrow";fv.SelectDay(null);FlowClick("undo · ⌘Z");
        Check(fv.CaptureBox.Text=="newer today"&&fs.Flow.Snapshot().StackItems.Single(i=>i.Id==b).Minutes==25,"Task undo clobbered live editor or did not undo task");
        Check(fv.SelectCouldDo()&&fv.CaptureBox.Text=="newer possibility"&&fv.SelectDay(day)&&fv.CaptureBox.Text=="newer tomorrow","Task undo clobbered newer inactive scoped drafts");
        fv.SelectCouldDo();fv.CaptureBox.Text="possibility pending";fv.SelectDay(null);fv.SelectTask(b);Jobs();
        Render(fw,"flow-today");
        fv.SelectDay(new DateOnly(2026,10,7));fv.CaptureBox.Text="planned future task";fv.Submit();Jobs();Render(fw,"flow-tomorrow");
        Check(fs.Flow.Snapshot().StackItems.Single(i=>i.Title=="planned future task").NotBefore==new DateOnly(2026,10,7)&&fs.Flow.Snapshot().StackItems.All(i=>i.StartedAt==null),"Planning started timer or lost date");
        fv.CaptureBox.Text="future pending";fv.SelectDay(null);fv.CaptureBox.Text="close pending";
        var path=fs.Flow.FilePath;File.Delete(path+".bak");Directory.CreateDirectory(path+".bak");fw.Close();Jobs();
        Check(fw.IsVisible&&fv.CaptureBox.Text=="close pending"&&!fs.IsQuitting,"Failed Flow close discarded pending draft");
        Directory.Delete(path+".bak");fw.Width=640;fw.Height=710;home.Close();Jobs();
        Check(!home.IsVisible&&fw.IsVisible&&!fs.IsQuitting,"Home close killed Flow-only session");
        Check(fs.TryQuit(),"Flow-only quit failed");Jobs();
    }
    using(var fs=new WorkspaceSession(flowRoot))
    {
        Check(fs.Flow is not null&&fs.FlowWindow is null&&fs.Flow.Snapshot().StackItems.All(i=>i.StartedAt==null),"Restart opened popup or auto-started idle Flow");
        fs.OpenHome();fs.OpenFlow();Jobs();var fv=fs.FlowWindow!.View;
        Check(fv.CaptureBox.Text=="close pending"&&fs.FlowWindow.Width==640&&fs.FlowWindow.Height==710,"Flow restart lost draft/geometry");
        Check(fv.SelectCouldDo()&&fv.CaptureBox.Text=="possibility pending","Restart lost Could do draft");
        Check(fv.SelectDay(new DateOnly(2026,10,7))&&fv.CaptureBox.Text=="future pending","Restart lost future draft");
        Check(fs.TryQuit(),"Reopened Flow quit failed");Jobs();
    }

    using(var fs=new WorkspaceSession(Path.Combine(root,"flow-final-close")))
    {
        var home=fs.OpenHome();fs.OpenFlow();Jobs();home.Close();Jobs();
        var backup=fs.Store.FilePath+".bak";File.Delete(backup);Directory.CreateDirectory(backup);
        fs.FlowWindow!.Close();Jobs();
        Check(home.IsVisible&&fs.FlowWindow is null&&!fs.IsQuitting,"Last Flow close plus workspace failure left invisible live app");
        Directory.Delete(backup);Check(fs.TryQuit(),"Failed final-close recovery could not quit after repair");Jobs();
    }
    var badFlowRoot=Path.Combine(root,"flow-invalid");Directory.CreateDirectory(badFlowRoot);
    var invalidFlow="{\"Version\":999}";File.WriteAllText(Path.Combine(badFlowRoot,"flow.json"),invalidFlow);
    using(var fs=new WorkspaceSession(badFlowRoot))
    {
        var home=fs.OpenHome();Jobs();
        Check(home.IsVisible&&!fs.OpenFlow()&&File.ReadAllText(Path.Combine(badFlowRoot,"flow.json"))==invalidFlow,"Invalid Flow prevented home or replaced original file");
        Check(fs.TryQuit(),"Invalid unopened Flow blocked normal Miche quit");Jobs();
    }

    // Exercise actual pointer routing in a separate fixture, leaving the lifecycle fixture intact.
    using (var deskSession = new WorkspaceSession(Path.Combine(root, "desk")))
    {
        var deskWindow = deskSession.OpenHome(); Jobs(); deskSession.OpenDump(); Jobs();
        var cardView = (DumpView)Named<ContentControl>(deskWindow, "DumpHost").Content!;
        var panel = Named<WidgetDesk>(deskWindow, "DeskPanel");
        var header = Named<Border>(cardView, "HeaderBar");
        Point HeaderPoint() => header.TranslatePoint(new Point(180, 18), deskWindow)!.Value;
        var originalCard = Named<ContentControl>(deskWindow, "DumpHost").Bounds;
        var originalText = File.ReadAllText(deskSession.Store.FilePath);
        var start = HeaderPoint();
        deskWindow.MouseDown(start, MouseButton.Left); deskWindow.MouseMove(start + new Vector(2, 2));
        deskWindow.MouseUp(start + new Vector(2, 2), MouseButton.Left); Jobs();
        Check(File.ReadAllText(deskSession.Store.FilePath) == originalText, "Subthreshold press changed saved placement");
        start = HeaderPoint(); deskWindow.MouseDown(start, MouseButton.Left); deskWindow.MouseMove(start + new Vector(80,72)); Jobs();
        Check(File.ReadAllText(deskSession.Store.FilePath) == originalText, "Drag persisted before release");
        deskWindow.MouseUp(start + new Vector(80,72), MouseButton.Left); Jobs();
        var moved = Named<ContentControl>(deskWindow, "DumpHost").Bounds;
        var placement = deskSession.Store.Snapshot.Placements.Single();
        Check(moved.Y == 72 && Math.Abs(moved.X - originalCard.X - 80) <= 4 && placement.PosY == 72 && placement.PosX >= 0,
            $"Header drag lost grab offset or didn't save original={originalCard} moved={moved} placement={System.Text.Json.JsonSerializer.Serialize(placement)} header={header.Bounds} panel={panel.Bounds}");
        var durable = File.ReadAllText(deskSession.Store.FilePath);
        start = HeaderPoint(); deskWindow.MouseDown(start, MouseButton.Left); deskWindow.MouseMove(start + new Vector(-64,48)); Jobs();
        deskWindow.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); deskWindow.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None); Jobs();
        deskWindow.MouseUp(start + new Vector(-64,48), MouseButton.Left); Jobs();
        Check(Named<ContentControl>(deskWindow, "DumpHost").Bounds == moved && File.ReadAllText(deskSession.Store.FilePath) == durable,
            "Escape didn't roll back entire drag");
        IPointer? pointer = null;
        panel.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer, RoutingStrategies.Tunnel, handledEventsToo: true);
        start = HeaderPoint(); deskWindow.MouseDown(start, MouseButton.Left); deskWindow.MouseMove(start + new Vector(-40,48)); Jobs();
        pointer!.Capture(null); Jobs(); deskWindow.MouseUp(start, MouseButton.Left); Jobs();
        Check(Named<ContentControl>(deskWindow, "DumpHost").Bounds == moved && File.ReadAllText(deskSession.Store.FilePath) == durable,
            "Pointer loss committed a partial drag");
        var grip = Named<Border>(cardView, "ResizeGrip");
        var resizeAt = grip.TranslatePoint(new Point(8,8), deskWindow)!.Value;
        deskWindow.MouseDown(resizeAt, MouseButton.Left); deskWindow.MouseMove(resizeAt + new Vector(-56,64));
        deskWindow.MouseUp(resizeAt + new Vector(-56,64), MouseButton.Left); Jobs();
        var resized = Named<ContentControl>(deskWindow, "DumpHost").Bounds;
        Check(resized.Width < moved.Width && resized.Height >= moved.Height + 60 && resized.X == moved.X && resized.Y == moved.Y,
            "Card resize moved origin or didn't change dimensions");
        var editorAt = cardView.Editor.TranslatePoint(new Point(20,20), deskWindow)!.Value;
        durable = File.ReadAllText(deskSession.Store.FilePath);
        deskWindow.MouseDown(editorAt, MouseButton.Left); deskWindow.MouseMove(editorAt + new Vector(45,10));
        deskWindow.MouseUp(editorAt + new Vector(45,10), MouseButton.Left); Jobs();
        Check(Named<ContentControl>(deskWindow, "DumpHost").Bounds == resized && File.ReadAllText(deskSession.Store.FilePath) == durable,
            "Editor selection dragged the card");
        panel.Focus(); deskWindow.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.Alt); deskWindow.KeyReleaseQwerty(PhysicalKey.ArrowDown, RawInputModifiers.Alt); Jobs();
        Check(Named<ContentControl>(deskWindow, "DumpHost").Bounds.Y == resized.Y + 8, "Keyboard move failed");
        panel.Focus(); deskWindow.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.Alt | RawInputModifiers.Shift);
        deskWindow.KeyReleaseQwerty(PhysicalKey.ArrowDown, RawInputModifiers.Alt | RawInputModifiers.Shift); Jobs();
        Check(Named<ContentControl>(deskWindow, "DumpHost").Bounds.Height == resized.Height + 8, "Keyboard resize failed");
        var beforeFailure = Named<ContentControl>(deskWindow, "DumpHost").Bounds;
        File.Delete(deskSession.Store.FilePath + ".bak"); Directory.CreateDirectory(deskSession.Store.FilePath + ".bak");
        durable = File.ReadAllText(deskSession.Store.FilePath);
        start = HeaderPoint(); deskWindow.MouseDown(start, MouseButton.Left); deskWindow.MouseMove(start + new Vector(-32,48));
        deskWindow.MouseUp(start + new Vector(-32,48), MouseButton.Left); Jobs();
        Check(Named<ContentControl>(deskWindow, "DumpHost").Bounds == beforeFailure && File.ReadAllText(deskSession.Store.FilePath) == durable,
            "Failed drag save didn't restore durable layout");
        Directory.Delete(deskSession.Store.FilePath + ".bak");
        deskWindow.Width = 480; Jobs();
        var narrow = Named<ContentControl>(deskWindow, "DumpHost").Bounds;
        Check(narrow.X >= 0 && narrow.Right <= panel.Bounds.Width + .1 && narrow.Width >= 320,
            "Narrowing made the card unreachable");
        deskWindow.Width = 980; Jobs();
        Render(deskWindow, "desk-moved");
        Press(cardView, "PopOutButton"); Press(cardView, "DockButton");
        Check(Named<ContentControl>(deskWindow, "DumpHost").Bounds == beforeFailure, "Popout/dock lost desk placement");
        Check(deskSession.TryQuit(), "Desk fixture quit failed"); Jobs();
        using var reopenedDesk = new WorkspaceSession(Path.Combine(root, "desk"));
        var reopenedWindow = reopenedDesk.OpenHome(); Jobs();
        Check(Named<ContentControl>(reopenedWindow, "DumpHost").Bounds == beforeFailure, "Restart lost desk position or dimensions");
        reopenedDesk.TryQuit(); Jobs();
    }

    using(var clips=new WorkspaceSession(Path.Combine(root,"clipboard-ui")))
    {
        var cw=clips.OpenHome();Jobs();Check(cw.OpenCommands(),"Clipboard command search failed");
        Named<TextBox>(cw,"CommandBox").Text="/clipboard";Jobs();
        Named<TextBox>(cw,"CommandBox").RaiseEvent(new KeyEventArgs {RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter});Jobs();
        var cv=Named<ContentControl>(cw,"ClipboardHost").Content as ClipboardView;
        Check(cv is not null&&cv.IsEffectivelyVisible&&!Named<StackPanel>(cw,"EmptyBoard").IsVisible,"Clipboard widget didn't open on desk");
        cv!.LabelEditor.Text="example profile";cv.LinkEditor.Text="https://example.com/profile";Check(cv.AddLink(),"Clipboard add link failed");Jobs();
        Check(clips.Store.Snapshot.Clipboard.Entries.Single().Url=="https://example.com/profile","Clipboard UI lost link");
        clips.OpenDump();Jobs();Check(cv.IsEffectivelyVisible&&Named<ContentControl>(cw,"DumpHost").IsEffectivelyVisible,"Clipboard displaced Dump");
        cv.LinkEditor.Text="https://www.linkedin.com/in/demo";Check(cv.AddLink(),"One-field link add failed");Jobs();
        Check(clips.Store.Snapshot.Clipboard.Entries.Last().Label=="LinkedIn","Clipboard URL label wasn't inferred");
        cv.LinkEditor.Text="https://example.com/unsent";Check(clips.PopoutClipboard(),"Clipboard popout failed");Jobs();
        Check(ReferenceEquals(clips.ClipboardWindow!.Viewport.Content,cv)&&cv.LinkEditor.Text=="https://example.com/unsent"&&!Named<ContentControl>(cw,"ClipboardHost").IsVisible,"Clipboard transfer lost draft or duplicated view");
        Check(clips.DockClipboard(),"Clipboard dock failed");Jobs();Check(ReferenceEquals(Named<ContentControl>(cw,"ClipboardHost").Content,cv),"Clipboard dock lost original view");
        cv.LinkEditor.Text="";
        cv.CopyUrl(clips.Store.Snapshot.Clipboard.Entries.First()).GetAwaiter().GetResult();
        Check(cw.Clipboard!.TryGetTextAsync().GetAwaiter().GetResult()=="https://example.com/profile","Clipboard chip didn't copy exact URL");
        Render(cw,"clipboard-widget");Check(clips.TryQuit(),"Clipboard quit failed");Jobs();
    }
    using(var groups=new WorkspaceSession(Path.Combine(root,"vision-group-ui")))
    {
        var gw=groups.OpenHome();Jobs();gw.ToggleVision();Jobs();var v=Named<VisionView>(gw,"VisionOverlay");
        v.AddShape("rounded-rectangle",new Point(80,150),100,80);v.AddShape("ellipse",new Point(230,150),100,80);Jobs();
        var original=groups.Store.Snapshot.VisionBoards.Single().Items.ToArray();
        var a=original[0].Id;var b=original[1].Id;
        var sa=v.ObjectControls[a].TranslatePoint(new Point(15,15),gw)!.Value;var sb=v.ObjectControls[b].TranslatePoint(new Point(15,15),gw)!.Value;
        gw.MouseDown(sa,MouseButton.Left);gw.MouseUp(sa,MouseButton.Left);Jobs();
        gw.MouseDown(sb,MouseButton.Left,RawInputModifiers.Shift);gw.MouseUp(sb,MouseButton.Left,RawInputModifiers.Shift);Jobs();Check(v.SelectedIds.Count==2,"Routed Shift-click selection failed");
        var before=File.ReadAllText(groups.Store.FilePath);var start=v.ObjectControls[a].TranslatePoint(new Point(30,20),gw)!.Value;
        gw.MouseDown(start,MouseButton.Left);gw.MouseMove(start+new Vector(50,30));Jobs();Check(File.ReadAllText(groups.Store.FilePath)==before,"Group saved before release");
        gw.MouseUp(start+new Vector(50,30),MouseButton.Left);Jobs();var moved=groups.Store.Snapshot.VisionBoards.Single().Items.ToArray();
        Check(moved[0].Left==130&&moved[1].Left==280&&moved.All(i=>i.Top==180),"Group movement lost spacing");
        v.RaiseEvent(new KeyEventArgs {RoutedEvent=InputElement.KeyDownEvent,Key=Key.OemPeriod,KeyModifiers=KeyModifiers.Meta|KeyModifiers.Shift});Jobs();
        Check(groups.Store.Snapshot.VisionBoards.Single().Items.All(i=>Math.Abs(i.Width-110)<.001),"Cmd Shift > didn't resize group");
        v.RaiseEvent(new KeyEventArgs {RoutedEvent=InputElement.KeyDownEvent,Key=Key.OemComma,KeyModifiers=KeyModifiers.Meta|KeyModifiers.Shift});Jobs();
        Check(groups.Store.Snapshot.VisionBoards.Single().Items.All(i=>Math.Abs(i.Width-100)<.001),"Cmd Shift < didn't reverse size step");
        var copied=v.CopySelection();Check(copied is not null&&v.PasteSelection(copied),"Group clipboard failed");Jobs();
        var all=groups.Store.Snapshot.VisionBoards.Single().Items;
        Check(all.Count==4&&v.SelectedIds.Count==2&&v.SelectedIds.All(id=>id!=a&&id!=b),"Paste reused original identities or lost group selection");
        Check(all.Last().Left-all[^2].Left==150,"Paste lost relative spacing");
        v.SelectObject(v.SelectedIds.First(),true);Check(v.SelectedIds.Count==1,"Shift-toggle selection failed");
        v.Escape();Jobs();var left=v.BoardCanvas.TranslatePoint(new Point(60,130),gw)!.Value;var right=v.BoardCanvas.TranslatePoint(new Point(420,300),gw)!.Value;
        gw.MouseDown(left,MouseButton.Left);gw.MouseMove(right);gw.MouseUp(right,MouseButton.Left);Jobs();Check(v.SelectedIds.Count==4,"Marquee didn't select intersecting objects");
        v.SetZoom(.5);Jobs();start=v.ObjectControls[a].TranslatePoint(new Point(20,20),gw)!.Value;before=File.ReadAllText(groups.Store.FilePath);
        gw.MouseDown(start,MouseButton.Left);gw.MouseMove(start+new Vector(20,20));gw.KeyPressQwerty(PhysicalKey.Escape,RawInputModifiers.None);gw.KeyReleaseQwerty(PhysicalKey.Escape,RawInputModifiers.None);gw.MouseUp(start+new Vector(20,20),MouseButton.Left);Jobs();
        Check(File.ReadAllText(groups.Store.FilePath)==before,"Escape saved cancelled group move");
        v.Escape();v.BeginText(new Point(450,400));Jobs();v.DraftEditor!.Text="tiny";Check(v.CommitDraft(),"Tight text commit failed");Jobs();
        var text=groups.Store.Snapshot.VisionBoards.Single().Items.Single(i=>i.Kind=="text");Check(text.Width<100&&text.Height<50,"Short text retained a wide empty box");
        Check(v.ToggleTextFrame(text.Id),"Round text frame failed");Jobs();text=groups.Store.Snapshot.VisionBoards.Single().Items.Single(i=>i.Kind=="text");Check(text.RoundedFrame&&text.Width<120,"Frame didn't fit text");
        var framedCopy=v.CopySelection();Check(framedCopy is not null&&v.PasteSelection(framedCopy),"Framed text copy failed");Jobs();
        Check(groups.Store.Snapshot.VisionBoards.Single().Items.Count(i=>i.RoundedFrame)==2,"Text frame was split or lost during paste");
        Check(v.FitBoard(),"Fit after framed paste failed");Jobs();gw.MouseMove(new Point(0,0));Render(gw,"vision-group-controls");Check(groups.TryQuit(),"Group window quit failed");Jobs();
    }
    using(var shapes=new WorkspaceSession(Path.Combine(root,"vision-shapes-zoom")))
    {
        var sw=shapes.OpenHome();Jobs();sw.ToggleVision();Jobs();
        var view=Named<VisionView>(sw,"VisionOverlay");var miche=shapes.Store.Snapshot.Index.RootMicheId;
        Check(view.AddShape("horizontal-line",new Point(80,120),240,16),"Horizontal line creation failed");
        Check(view.AddShape("vertical-line",new Point(380,120),16,200),"Vertical line creation failed");
        Check(view.AddShape("rounded-rectangle",new Point(80,360),240,160),"Rounded rectangle creation failed");
        Check(view.AddShape("ellipse",new Point(430,360),180,180),"Circle creation failed");Jobs();
        var saved=File.ReadAllText(shapes.Store.FilePath);var width=view.BoardCanvas.Width;
        Check(view.SetZoom(.5),"Zoom out failed");Jobs();
        Check(view.BoardCanvas.Width>width && File.ReadAllText(shapes.Store.FilePath)==saved,"Zoom mutated data or failed to expand space");
        var line=shapes.Store.Snapshot.VisionBoards.Single().Items.First();
        var start=view.ObjectControls[line.Id].TranslatePoint(new Point(30,8),sw)!.Value;
        sw.MouseDown(start,MouseButton.Left);sw.MouseUp(start+new Vector(50,25),MouseButton.Left);Jobs();
        var moved=shapes.Store.Snapshot.VisionBoards.Single().Items.Single(i=>i.Id==line.Id);
        Check(Math.Abs(moved.Left-180)<.01 && Math.Abs(moved.Top-170)<.01 && moved.Width==240,"Dragging at 50% lost world coordinates or resized line");
        var grip=view.ObjectControls[line.Id].GetVisualDescendants().OfType<Border>().Single(b=>b.Name=="VisionResizeGrip");
        var gp=grip.TranslatePoint(new Point(8,8),sw)!.Value;
        sw.MouseDown(gp,MouseButton.Left);sw.MouseUp(gp+new Vector(30,25),MouseButton.Left);Jobs();
        var resized=shapes.Store.Snapshot.VisionBoards.Single().Items.Single(i=>i.Id==line.Id);
        Check(Math.Abs(resized.Width-300)<.01 && resized.Height==16,"Line resize at zoom changed thickness or lost scale");
        var blank=view.BoardCanvas.TranslatePoint(new Point(900,650),sw)!.Value;
        sw.MouseDown(blank,MouseButton.Left);sw.MouseUp(blank,MouseButton.Left);Jobs();
        Check(view.DraftEditor is not null && Math.Abs(Canvas.GetLeft(view.DraftEditor)-900)<.01,"Writing at zoom lost coordinates");
        view.DraftEditor!.Text="draft survives zoom";Check(view.SetZoom(1),"Zoom draft commit failed");Jobs();
        Check(shapes.Store.Snapshot.VisionBoards.Single().Items.Any(i=>i.Text=="draft survives zoom"),"Zoom lost draft");
        saved=File.ReadAllText(shapes.Store.FilePath);
        Check(view.FitBoard() && view.ZoomScale>=.25 && view.ZoomScale<=2,"Fit produced invalid scale");Jobs();
        Check(File.ReadAllText(shapes.Store.FilePath)==saved,"Fit persisted geometry");
        Check(view.SetZoom(100) && view.ZoomScale==2 && view.SetZoom(.001) && view.ZoomScale==.25,"Zoom limits failed");
        Check(!view.SetZoom(double.NaN),"Invalid zoom accepted");
        view.FitBoard();Jobs();Render(sw,"vision-shapes-zoom");
        view.Delete(line.Id);Jobs();Check(shapes.Store.Snapshot.VisionBoards.Single().Items.Single(i=>i.Id==line.Id).DeletedAt is not null,"Shape recovery deletion failed");
        shapes.Store.SetVisionDeleted(miche,line.Id,false);Jobs();
        Check(view.ObjectControls.ContainsKey(line.Id),"Shape restore failed");
        Check(shapes.TryQuit(),"Shape quit failed");Jobs();
    }
    using (var visionSession = new WorkspaceSession(Path.Combine(root,"vision-ui")))
    {
        var vw = visionSession.OpenHome(); Jobs(); var overlay = Named<VisionView>(vw,"VisionOverlay");
        var firstId = visionSession.Store.Snapshot.Index.RootMicheId;
        var dumpId = Guid.Empty;
        visionSession.OpenDump(); Jobs();
        var vd = (DumpView)Named<ContentControl>(vw,"DumpHost").Content!; dumpId = vd.WidgetId;
        vd.Editor.Focus(); vw.KeyTextInput("v");
        Check(!overlay.IsVisible && vd.Editor.Text == "v", "Typing v triggered Vision");
        Named<Button>(vw,"SpaceMenuButton").Focus(); vw.KeyPressQwerty(PhysicalKey.V,RawInputModifiers.Shift); vw.KeyReleaseQwerty(PhysicalKey.V,RawInputModifiers.Shift); Jobs();
        Check(!overlay.IsVisible, "Modified V triggered Vision");
        vw.KeyPressQwerty(PhysicalKey.V,RawInputModifiers.None); vw.KeyReleaseQwerty(PhysicalKey.V,RawInputModifiers.None); Jobs();
        Check(overlay.IsVisible, "Bare V didn't open Vision");
        var at = overlay.BoardCanvas.TranslatePoint(new Point(80,120),vw)!.Value;
        vw.MouseDown(at,MouseButton.Left); vw.MouseUp(at,MouseButton.Left); Jobs();
        Check(overlay.DraftEditor is not null && overlay.DraftEditor.IsFocused && Canvas.GetLeft(overlay.DraftEditor) == 80 && Canvas.GetTop(overlay.DraftEditor) == 120,
            "Click-to-write lost canvas coordinates/focus");
        vw.KeyTextInput("vision first");
        vw.KeyPressQwerty(PhysicalKey.Enter,RawInputModifiers.Shift); vw.KeyReleaseQwerty(PhysicalKey.Enter,RawInputModifiers.Shift); vw.KeyTextInput("line two");
        overlay.DraftEditor!.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); Jobs();
        var textItem = visionSession.Store.Snapshot.VisionBoards.Single().Items.Single(); var textId = textItem.Id;
        Check(overlay.DraftEditor is null && textItem.Text.Contains("\n") && textItem.Left == 80 && textItem.Top == 120, "Vision Enter/ShiftEnter commit failed");
        vw.KeyPressQwerty(PhysicalKey.Escape,RawInputModifiers.None); vw.KeyReleaseQwerty(PhysicalKey.Escape,RawInputModifiers.None); Jobs();
        Check(overlay.IsVisible && overlay.SelectedId is null, "Escape skipped selection before close");
        vw.KeyPressQwerty(PhysicalKey.Escape,RawInputModifiers.None); vw.KeyReleaseQwerty(PhysicalKey.Escape,RawInputModifiers.None); Jobs();
        Check(!overlay.IsVisible, "Escape didn't close clear Vision");
        vw.ToggleVision(); Jobs(); overlay.BeginText(new Point(400,80)); Jobs(); vw.KeyTextInput("cancel me");
        vw.KeyPressQwerty(PhysicalKey.Escape,RawInputModifiers.None); vw.KeyReleaseQwerty(PhysicalKey.Escape,RawInputModifiers.None); Jobs();
        Check(overlay.IsVisible && overlay.DraftEditor is null && visionSession.Store.Snapshot.VisionBoards.Single().Items.Count == 1, "Escape committed unfinished text");
        overlay.Rotate(textId,-5); Jobs(); Check(visionSession.Store.Snapshot.VisionBoards.Single().Items.Single().Rotation == -5, "Counterclockwise rotation failed");
        overlay.Rotate(textId,5); Jobs(); Check(visionSession.Store.Snapshot.VisionBoards.Single().Items.Single().Rotation == 0, "Clockwise rotation failed");
        var objectAt = overlay.ObjectControls[textId].TranslatePoint(new Point(30,12),vw)!.Value;
        var beforeVision = File.ReadAllText(visionSession.Store.FilePath);
        vw.MouseDown(objectAt,MouseButton.Left); vw.MouseMove(objectAt+new Vector(48,40)); Jobs();
        Check(File.ReadAllText(visionSession.Store.FilePath) == beforeVision, "Canvas move saved before release");
        vw.MouseUp(objectAt+new Vector(48,40),MouseButton.Left); Jobs();
        var movedText = visionSession.Store.Snapshot.VisionBoards.Single().Items.Single();
        Check(movedText.Left == 128 && movedText.Top == 160, $"Canvas object drag lost grab offset persisted={System.Text.Json.JsonSerializer.Serialize(movedText)} editor={overlay.DraftEditor?.Text} object={overlay.ObjectControls.GetValueOrDefault(textId)?.Bounds} point={objectAt} canvas={overlay.BoardCanvas.Bounds}");
        objectAt = overlay.ObjectControls[textId].TranslatePoint(new Point(30,12),vw)!.Value;
        vw.MouseDown(objectAt,MouseButton.Left); vw.MouseMove(objectAt+new Vector(64,32)); Jobs();
        vw.KeyPressQwerty(PhysicalKey.Escape,RawInputModifiers.None); vw.KeyReleaseQwerty(PhysicalKey.Escape,RawInputModifiers.None);
        vw.MouseUp(objectAt+new Vector(64,32),MouseButton.Left); Jobs();
        Check(Canvas.GetLeft(overlay.ObjectControls[textId]) == 128 && visionSession.Store.Snapshot.VisionBoards.Single().Items.Single().Left == 128,
            "Canvas Escape didn't roll back move");
        // Select at a different point, avoiding a deliberate double-click edit.
        objectAt = overlay.ObjectControls[textId].TranslatePoint(new Point(100,12),vw)!.Value;
        vw.MouseDown(objectAt,MouseButton.Left); vw.MouseUp(objectAt,MouseButton.Left); Jobs();
        var textGrip = overlay.ObjectControls[textId].GetVisualDescendants().OfType<Border>().Single(b=>b.Name=="VisionResizeGrip");
        var gripAt = textGrip.TranslatePoint(new Point(8,8),vw)!.Value;
        vw.MouseDown(gripAt,MouseButton.Left); vw.MouseMove(gripAt+new Vector(70,0)); vw.MouseUp(gripAt+new Vector(70,0),MouseButton.Left); Jobs();
        var scaledText = visionSession.Store.Snapshot.VisionBoards.Single().Items.Single();
        Check(scaledText.FontSize > movedText.FontSize && scaledText.Width > movedText.Width, "Text resize didn't persist font AND wrap width");
        overlay.BeginText(new Point(scaledText.Left,scaledText.Top),scaledText); Jobs(); vw.KeyTextInput(" edited"); overlay.CommitDraft(); Jobs();
        var edited = visionSession.Store.Snapshot.VisionBoards.Single().Items.Single();
        Check(edited.FontSize == scaledText.FontSize && edited.Width>8 && edited.Width<=Math.Max(280,scaledText.Width), "Editing reset text scale or failed to hug content");
        var source = Path.Combine(AppContext.BaseDirectory,"fixtures","vision.png"); var sourceBytes = File.ReadAllBytes(source);
        Check(overlay.ImportImage(source,new Point(350,300)), "PNG import failed"); Jobs();
        var image = visionSession.Store.Snapshot.VisionBoards.Single().Items.Single(i=>i.Kind=="image");
        var ownedPath = visionSession.Store.VisionAssetPath(firstId,image.FileName!);
        Check(File.ReadAllBytes(ownedPath).SequenceEqual(sourceBytes) && File.ReadAllBytes(source).SequenceEqual(sourceBytes), "Import didn't copy or changed original");
        var imageGrip = overlay.ObjectControls[image.Id].GetVisualDescendants().OfType<Border>().Single(b=>b.Name=="VisionResizeGrip");
        gripAt = imageGrip.TranslatePoint(new Point(8,8),vw)!.Value;
        vw.MouseDown(gripAt,MouseButton.Left); vw.MouseMove(gripAt+new Vector(96,64)); vw.MouseUp(gripAt+new Vector(96,64),MouseButton.Left); Jobs();
        var resizedImage = visionSession.Store.Snapshot.VisionBoards.Single().Items.Single(i=>i.Id==image.Id);
        Check(resizedImage.Width > image.Width && Math.Abs(resizedImage.Width/resizedImage.Height-image.Width/image.Height)<.001, "Image resize changed aspect ratio");
        overlay.Delete(image.Id); Jobs();
        Check(File.Exists(ownedPath) && visionSession.Store.Snapshot.VisionBoards.Single().Items.Single(i=>i.Id==image.Id).DeletedAt is not null,
            "Recoverable image deletion removed owned asset");
        visionSession.Store.SetVisionDeleted(firstId,image.Id,false); Jobs();
        Check(overlay.ObjectControls.ContainsKey(image.Id) && File.Exists(ownedPath), "Image restore failed");
        Check(overlay.ImportImage(Path.Combine(AppContext.BaseDirectory,"fixtures","vision.jpg"),new Point(500,450)), "JPEG import failed"); Jobs();
        var assetCount = Directory.GetFiles(Path.GetDirectoryName(ownedPath)!).Length;
        var invalidImage = Path.Combine(root,"broken.png"); File.WriteAllText(invalidImage,"not an image");
        Check(!overlay.ImportImage(invalidImage,new Point(20,20)) && Directory.GetFiles(Path.GetDirectoryName(ownedPath)!).Length == assetCount,
            "Invalid import left an asset");
        File.Delete(visionSession.Store.FilePath+".bak"); Directory.CreateDirectory(visionSession.Store.FilePath+".bak");
        var durableVision = File.ReadAllText(visionSession.Store.FilePath);
        Check(!overlay.ImportImage(source,new Point(20,20)) && Directory.GetFiles(Path.GetDirectoryName(ownedPath)!).Length == assetCount &&
            File.ReadAllText(visionSession.Store.FilePath) == durableVision, "Failed image save left orphan/phantom item");
        objectAt = overlay.ObjectControls[textId].TranslatePoint(new Point(150,12),vw)!.Value;
        var failedMoveLeft = Canvas.GetLeft(overlay.ObjectControls[textId]);
        vw.MouseDown(objectAt,MouseButton.Left); vw.MouseMove(objectAt+new Vector(32,32)); vw.MouseUp(objectAt+new Vector(32,32),MouseButton.Left); Jobs();
        Check(Canvas.GetLeft(overlay.ObjectControls[textId]) == failedMoveLeft && File.ReadAllText(visionSession.Store.FilePath) == durableVision,
            "Failed canvas gesture didn't roll back durable geometry");
        overlay.BeginText(new Point(200,550)); Jobs(); vw.KeyTextInput("keep failed draft");
        Check(!overlay.CommitDraft() && overlay.DraftEditor?.Text == "keep failed draft" && !visionSession.TryQuit() && vw.IsVisible,
            "Failed Vision save/quit lost draft");
        Directory.Delete(visionSession.Store.FilePath+".bak"); overlay.CancelDraft();
        // A vertical scroll must enter the canvas coordinate system exactly once.
        overlay.BoardScroll.Offset = new Vector(0,400); Jobs();
        at = overlay.BoardCanvas.TranslatePoint(new Point(100,700),vw)!.Value;
        vw.MouseDown(at,MouseButton.Left); vw.MouseUp(at,MouseButton.Left); Jobs(); vw.KeyTextInput("scrolled text"); overlay.CommitDraft(); Jobs();
        Check(visionSession.Store.Snapshot.VisionBoards.Single().Items.Single(i=>i.Text=="scrolled text").Top == 700, "Canvas click counted scroll offset twice");
        overlay.BoardScroll.Offset = default; Jobs(); Render(vw,"vision");
        Check(overlay.ImportImage(Path.Combine(AppContext.BaseDirectory,"fixtures","tall.png"),new Point(40,40)), "Tall PNG import rejected"); Jobs();
        var tall = visionSession.Store.Snapshot.VisionBoards.Single().Items.Last();
        Check(tall.Width > 0 && tall.Height == 1200 && Math.Abs(tall.Width/tall.Height-.05)<.000001 && File.Exists(visionSession.Store.VisionAssetPath(firstId,tall.FileName!)),
            "Tall import truncated width or lost asset");
        Check(overlay.ImportImage(Path.Combine(AppContext.BaseDirectory,"fixtures","wide.png"),new Point(40,40)), "Wide short PNG import rejected"); Jobs();
        var wide = visionSession.Store.Snapshot.VisionBoards.Single().Items.Last();
        Check(wide.Height > 0 && Math.Abs(wide.Width/wide.Height-8192)<.01, "Wide import truncated height");
        var boundary = new Miche.Mac.Models.VisionItem { Text="boundary", Left=350, Top=900, Width=600, Height=40, FontSize=8 };
        visionSession.Store.SaveVisionItem(firstId,boundary); Jobs(); overlay.BoardScroll.Offset = new Vector(0,800); Jobs();
        objectAt = overlay.ObjectControls[boundary.Id].TranslatePoint(new Point(40,12),vw)!.Value;
        vw.MouseDown(objectAt,MouseButton.Left); vw.MouseUp(objectAt,MouseButton.Left); Jobs();
        var boundaryGrip = overlay.ObjectControls[boundary.Id].GetVisualDescendants().OfType<Border>().Single(b=>b.Name=="VisionResizeGrip");
        gripAt = boundaryGrip.TranslatePoint(new Point(8,8),vw)!.Value;
        vw.MouseDown(gripAt,MouseButton.Left); overlay.BoardCanvas.Width = 500;
        vw.MouseMove(gripAt+new Vector(-150,0)); vw.MouseUp(gripAt+new Vector(-150,0),MouseButton.Left); Jobs();
        var constrained = visionSession.Store.Snapshot.VisionBoards.Single().Items.Single(i=>i.Id==boundary.Id);
        Check(constrained.FontSize == 8 && constrained.Width == 600, "Narrow canvas forced resize below hard minimum");
        overlay.BoardCanvas.Width = 10000; Jobs();
        boundaryGrip = overlay.ObjectControls[boundary.Id].GetVisualDescendants().OfType<Border>().Single(b=>b.Name=="VisionResizeGrip");
        gripAt = boundaryGrip.TranslatePoint(new Point(8,8),vw)!.Value;
        vw.MouseDown(gripAt,MouseButton.Left); vw.MouseMove(gripAt+new Vector(8000,0)); vw.MouseUp(gripAt+new Vector(8000,0),MouseButton.Left); Jobs();
        constrained = visionSession.Store.Snapshot.VisionBoards.Single().Items.Single(i=>i.Id==boundary.Id);
        Check(constrained.Width <= 4000 && constrained.FontSize >= 8 && constrained.FontSize <= 200, "Huge canvas preview produced invalid size");
        for (int r=0;r<80;r++) overlay.Rotate(boundary.Id,5);
        Check(Math.Abs(visionSession.Store.Snapshot.VisionBoards.Single().Items.Single(i=>i.Id==boundary.Id).Rotation)<=180, "Repeated rotation exceeded valid angles");
        overlay.BoardScroll.Offset = default; Jobs();
        var otherId = visionSession.Store.Create("separate vision"); Jobs();
        Check(overlay.ObjectControls.Count == 0, "Other niche inherited Vision objects");
        overlay.BeginText(new Point(60,80)); Jobs(); vw.KeyTextInput("other board"); overlay.CommitDraft(); Jobs();
        visionSession.SaveAllEditors(); visionSession.Store.Activate(firstId); Jobs();
        Check(overlay.ObjectControls.ContainsKey(textId) && !visionSession.Store.Snapshot.VisionBoards.Single(b=>b.MicheId==firstId).Items.Any(i=>i.Text=="other board"),
            "Vision space isolation failed");
        Check(visionSession.TryQuit(), "Vision fixture quit failed"); Jobs();
        using var visionRestart = new WorkspaceSession(Path.Combine(root,"vision-ui"));
        var restartedWindow = visionRestart.OpenHome(); Jobs(); restartedWindow.ToggleVision(); Jobs();
        var restoredBoard = visionRestart.Store.Snapshot.VisionBoards.Single(b=>b.MicheId==firstId);
        var restoredText = restoredBoard.Items.Single(i=>i.Id==textId);
        Check(restoredText.FontSize==scaledText.FontSize && restoredText.Width==edited.Width &&
            File.Exists(visionRestart.Store.VisionAssetPath(firstId,image.FileName!)) &&
            restoredBoard.Items.Single(i=>i.Id==tall.Id).Width == tall.Width && restoredBoard.Items.Single(i=>i.Id==wide.Id).Height == wide.Height, "Restart lost text scale or image ownership/aspect");
        visionRestart.Store.Delete(otherId); visionRestart.Store.Restore(otherId); Jobs();
        Check(visionRestart.Store.Snapshot.VisionBoards.Single(b=>b.MicheId==otherId).Items.Single().Text=="other board", "Niche restore lost Vision");
        visionRestart.TryQuit(); Jobs();
    }

    using(var pastSession=new WorkspaceSession(Path.Combine(root,"past-ui")))
    {
        var pw=pastSession.OpenHome();Jobs();pw.ToggleVision();Jobs();var pv=Named<VisionView>(pw,"VisionOverlay");
        var owner=pastSession.Store.Snapshot.Index.ActiveMicheId;
        pv.BeginText(new Point(100,90));Jobs();pw.KeyTextInput("old vision draft");
        pv.ShowStartFresh();Jobs();
        Check(pv.LifecycleDialog.IsVisible && pastSession.Store.Snapshot.VisionBoards.Single().Items.Single().Text=="old vision draft", "Start fresh didn't safely commit active text");
        var saveAnd=pv.LifecycleDialog.GetVisualDescendants().OfType<Button>().Single(b=>b.Content as string=="save & start fresh");
        saveAnd.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Jobs();
        var title=pv.LifecycleDialog.GetVisualDescendants().OfType<TextBox>().Single();title.Text="past season";
        title.RaiseEvent(new KeyEventArgs {RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter});Jobs();
        var artifactId=pastSession.Store.Snapshot.VisionArtifacts.Single().Id;
        Check(!pv.LifecycleDialog.IsVisible && pv.ObjectControls.Count==0 && pastSession.Store.Snapshot.VisionResets.Count==1, "Save/title/Enter didn't create past Vision and clear current");
        pv.BeginText(new Point(20,30));Jobs();pw.KeyTextInput("new current");pv.CommitDraft();Jobs();
        Check(pv.ImportImage(Path.Combine(AppContext.BaseDirectory,"fixtures","vision.png"),new Point(220,150)),"Current image import failed");Jobs();
        var currentImage=pastSession.Store.Snapshot.VisionBoards.Single().Items.Single(i=>i.Kind=="image");
        Check(pv.OpenArtifact(artifactId) && pv.ArtifactTargetId==artifactId && pv.ObjectControls.Count==1,"Opening past Vision replaced current or chose wrong target");
        var past=pastSession.Store.Snapshot.VisionArtifacts.Single().Items.Single();pv.BeginText(new Point(past.Left,past.Top),past);Jobs();pw.KeyTextInput(" expanded");pv.CommitDraft();Jobs();
        Check(pastSession.Store.Snapshot.VisionBoards.Single().Items.Any(i=>i.Text=="new current") && pastSession.Store.Snapshot.VisionArtifacts.Single().Items.Single().Text=="old vision draft expanded", "Past edit mutated current");
        Check(pv.ImportImage(Path.Combine(AppContext.BaseDirectory,"fixtures","vision.png"),new Point(300,180)),"Past Vision image import failed");Jobs();
        var pastImage=pastSession.Store.Snapshot.VisionArtifacts.Single().Items.Single(i=>i.Kind=="image");
        Check(pastImage.FileName!=currentImage.FileName && !pastSession.Store.Snapshot.VisionBoards.Single().Items.Any(i=>i.Id==pastImage.Id),"Artifact import changed current or image ownership");
        Check(!pv.SaveAndStartFresh("wrong target") && pastSession.Store.Snapshot.VisionBoards.Single().Items.Count==2,"Start fresh cleared from an artifact target");
        pv.BeginText(new Point(past.Left,past.Top),pastSession.Store.Snapshot.VisionArtifacts.Single().Items.Single(i=>i.Kind=="text"));Jobs();pw.KeyTextInput(" kept");
        File.Delete(pastSession.Store.FilePath+".bak");Directory.CreateDirectory(pastSession.Store.FilePath+".bak");
        Check(!pv.ReturnToCurrent() && pv.ArtifactTargetId==artifactId && pv.DraftEditor!.Text!.EndsWith(" kept"),"Failed return dropped artifact editor or changed target");
        Directory.Delete(pastSession.Store.FilePath+".bak");Check(pv.ReturnToCurrent() && pv.ArtifactTargetId is null && pv.ObjectControls.Count==2,"Return to current didn't preserve target draft");Jobs();
        pv.BeginText(new Point(60,300));Jobs();pw.KeyTextInput("unsaved clear draft");
        File.Delete(pastSession.Store.FilePath+".bak");Directory.CreateDirectory(pastSession.Store.FilePath+".bak");
        Check(!pv.SaveAndStartFresh("fails") && pv.DraftEditor!.Text=="unsaved clear draft" && pastSession.Store.Snapshot.VisionArtifacts.Count==1,
            "Failed text commit allowed reset or discarded live text");
        Directory.Delete(pastSession.Store.FilePath+".bak");Check(pv.SaveAndStartFresh("second season"),"Current save-and-clear failed after retry");Jobs();
        var second=pastSession.Store.Snapshot.VisionArtifacts.Last();
        Check(second.Items.Any(i=>i.FileName==currentImage.FileName) && File.Exists(pastSession.Store.VisionAssetPath(owner,currentImage.FileName!)),"Clear discarded snapshot image");
        pv.OpenArtifact(second.Id);Jobs();pv.Delete(second.Items.Single(i=>i.Kind=="image").Id);Jobs();
        Check(File.Exists(pastSession.Store.VisionAssetPath(owner,currentImage.FileName!)) && pastSession.Store.Snapshot.VisionResets.Last().Items.Any(i=>i.FileName==currentImage.FileName), "Artifact object deletion lost reset media");
        var headerPosition=Named<Border>(pw,"WindowHeader").TranslatePoint(new Point(0,0),pw)!.Value;
        Check(headerPosition.Y>=0 && Named<VisionView>(pw,"VisionOverlay").TranslatePoint(new Point(0,0),pw)!.Value.Y>=46,
            $"Vision focus scrolled integrated header away: header={headerPosition} overlay={Named<VisionView>(pw,"VisionOverlay").Bounds}");
        Render(pw,"past-vision");pv.ReturnToCurrent();pv.BeginText(new Point(30,40));Jobs();pw.KeyTextInput("clear without saving");
        Check(pv.SaveAndStartFresh(null),"Recoverable clear failed");Jobs();var reset=pastSession.Store.Snapshot.VisionResets.Last();
        pv.BeginText(new Point(10,10));Jobs();pw.KeyTextInput("newer work");pv.CommitDraft();Jobs();
        var recovered=pastSession.Store.RecoverVisionReset(owner,reset.Id);pv.OpenArtifact(recovered);Jobs();
        Check(pastSession.Store.Snapshot.VisionBoards.Single().Items.Single().Text=="newer work" && pv.ObjectControls.Count==1,"Recovery replaced newer current work");
        pastSession.SaveAllEditors();var other=pastSession.Store.Create("other past scope");Jobs();
        Check(pv.ArtifactTargetId is null && !pv.OpenArtifact(artifactId) && pv.ObjectControls.Count==0,"Past artifact leaked across Miche switch");
        pastSession.SaveAllEditors();pastSession.Store.Activate(owner);Jobs();pv.ShowPastVisions();Jobs();
        Check(pv.LifecycleDialog.GetVisualDescendants().OfType<Button>().Any(b=>b.Content as string=="open · past season"),"Past collection isn't discoverable");
        pastSession.TryQuit();Jobs();
        using var reopened=new WorkspaceSession(Path.Combine(root,"past-ui"));var rp=reopened.OpenHome();Jobs();rp.ToggleVision();Jobs();var rv=Named<VisionView>(rp,"VisionOverlay");
        Check(rv.OpenArtifact(artifactId) && reopened.Store.Snapshot.VisionArtifacts.Single(a=>a.Id==artifactId).Items.Any(i=>i.Text.EndsWith(" kept")) && File.Exists(reopened.Store.VisionAssetPath(owner,pastImage.FileName!)),"Restart lost saved artifact edits or imported media");
        reopened.TryQuit();Jobs();
    }

    using(var notifySession=new WorkspaceSession(Path.Combine(root,"notify-ui")))
    {
        var nw=notifySession.OpenHome();Jobs();nw.ToggleVision();Jobs();var nv=Named<VisionView>(nw,"VisionOverlay");
        var notifyOwner=notifySession.Store.Snapshot.Index.RootMicheId;
        notifySession.Store.Changed+=()=>throw new IOException("synthetic observer failure");
        Check(nv.ImportImage(Path.Combine(AppContext.BaseDirectory,"fixtures","vision.png"),new Point(40,40)) && notifySession.Store.LastNotificationWarning is not null,
            "Post-commit notification failure was treated as an import save failure");Jobs();
        var media=notifySession.Store.Snapshot.VisionBoards.Single().Items.Single().FileName!;
        Check(nv.SaveAndStartFresh("durable observer snapshot") && notifySession.Store.Snapshot.VisionArtifacts.Count==1 && notifySession.Store.Snapshot.VisionBoards.Single().Items.Count==0 &&
            Named<TextBlock>(nw,"Status").Text!.StartsWith("Saved on this Mac"),"Observer failure reported reset as failed or encouraged retry after commit");Jobs();
        notifySession.TryQuit();Jobs();
        using var reopen=new WorkspaceSession(Path.Combine(root,"notify-ui"));
        Check(reopen.Store.Snapshot.VisionArtifacts.Count==1 && reopen.Store.Snapshot.VisionResets.Count==1 && File.Exists(reopen.Store.VisionAssetPath(notifyOwner,media)),
            "Observer failure lost published media/snapshot or repeated reset");reopen.TryQuit();Jobs();
    }

    using(var scopeSession=new WorkspaceSession(Path.Combine(root,"scope-ui")))
    {
        var sw=scopeSession.OpenHome(); Jobs(); scopeSession.OpenDump(); Jobs();
        var sd=(DumpView)Named<ContentControl>(sw,"DumpHost").Content!; sd.Editor.Focus(); sw.KeyTextInput("canonical draft");
        sd.SwitchScope(true); Jobs(); Check(sd.Editor.Text=="" && Named<TextBlock>(sd,"ScopeLabel").Text!.Contains("local"), "Local scope didn't load a separate labeled draft");
        sw.KeyTextInput("local draft"); sd.SwitchScope(false); Jobs();
        Check(sd.Editor.Text=="canonical draft" && scopeSession.Store.Snapshot.Widgets.Single().LocalEditor.Text=="local draft", "Scope switch lost drafts");
        sd.Editor.Text="shared note"; sd.Editor.RaiseEvent(new KeyEventArgs {RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter}); Jobs();
        sd.SwitchScope(true); sd.Editor.Text="local note"; sd.Editor.RaiseEvent(new KeyEventArgs {RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter}); Jobs();
        Check(Named<StackPanel>(sd,"CaptureList").GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text=="local note") &&
            !Named<StackPanel>(sd,"CaptureList").GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text=="shared note"), "Local UI filtered by origin or showed shared record");
        sd.Editor.Text="keep through flush";
        Check(sd.ConfirmFlush() && scopeSession.Store.DumpEntries().Single().Text=="shared note" && sd.Editor.Text=="keep through flush", "Local flush lost shared notes or draft");
        Press(sd,"UndoFlushButton"); Check(scopeSession.Store.DumpEntries().Count==2,"UI undo flush failed");
        sd.SwitchScope(false); Jobs();
        Check(Named<StackPanel>(sd,"CaptureList").GetVisualDescendants().OfType<TextBlock>().Count(t=>t.Text is "local note" or "shared note")==2,"Canonical UI duplicated or omitted local record");
        sd.Editor.Text="outgoing after failure"; var before=File.ReadAllText(scopeSession.Store.FilePath);
        File.Delete(scopeSession.Store.FilePath+".bak"); Directory.CreateDirectory(scopeSession.Store.FilePath+".bak");
        sd.SwitchScope(true); Jobs(); Check(sd.Editor.Text=="outgoing after failure" && scopeSession.Store.Snapshot.Widgets.Single().CaptureScope=="canonical" && File.ReadAllText(scopeSession.Store.FilePath)==before,
            "Failed scope change switched or erased live editor");
        Directory.Delete(scopeSession.Store.FilePath+".bak");
        sd.SwitchScope(true); sd.Editor.CaretIndex=4;sd.Editor.SelectionStart=2;sd.Editor.SelectionEnd=5;var localCursor=sd.EditorSnapshot();Press(sd,"PopOutButton");
        sw.OpenCommands();Jobs();var searchBox=Named<TextBox>(sw,"CommandBox");searchBox.Text="/dump";
        searchBox.RaiseEvent(new KeyEventArgs {RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter});Jobs();
        Check(scopeSession.Store.Snapshot.Widgets.Single().CaptureScope=="canonical" && sd.Editor.Text=="outgoing after failure" && scopeSession.FloatingWindows.Count==1,
            "/dump didn't choose canonical while preserving a local floating draft");
        sw.OpenCommands();Jobs();searchBox.Text="/local-dump";
        searchBox.RaiseEvent(new KeyEventArgs {RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter});Jobs();
        Check(scopeSession.Store.Snapshot.Widgets.Single().CaptureScope=="local" && sd.Editor.Text=="keep through flush" && sd.Editor.SelectionStart==localCursor.SelectionStart && sd.Editor.SelectionEnd==localCursor.SelectionEnd && sd.Editor.CaretIndex==localCursor.CaretIndex,
            $"/local-dump didn't restore local text/caret/selection from canonical float expected={System.Text.Json.JsonSerializer.Serialize(localCursor)} actual={System.Text.Json.JsonSerializer.Serialize(sd.EditorSnapshot())} scope={scopeSession.Store.Snapshot.Widgets.Single().CaptureScope}");
        before=File.ReadAllText(scopeSession.Store.FilePath);File.Delete(scopeSession.Store.FilePath+".bak");Directory.CreateDirectory(scopeSession.Store.FilePath+".bak");
        scopeSession.OpenDump();Jobs();Check(sd.Editor.Text=="keep through flush" && scopeSession.Store.Snapshot.Widgets.Single().CaptureScope=="local" && File.ReadAllText(scopeSession.Store.FilePath)==before,"Failed OpenDump consumed floating draft or switched scope");
        Directory.Delete(scopeSession.Store.FilePath+".bak");sd.Editor.Text="keep through flush + retry";Jobs();
        using(var timerWait=new System.Threading.CancellationTokenSource(650)) Dispatcher.UIThread.MainLoop(timerWait.Token); Jobs();
        Check(scopeSession.Store.Snapshot.Widgets.Single().LocalEditor.Text=="keep through flush + retry","Autosave didn't resume after failed OpenDump on subsequent editing");
        Press(sd,"DockButton");
        Check(sd.Editor.Text=="keep through flush + retry" && ReferenceEquals(Named<ContentControl>(sw,"DumpHost").Content,sd), "Local draft lost during popout/dock");
        scopeSession.TryQuit(); Jobs();
        using var restart=new WorkspaceSession(Path.Combine(root,"scope-ui")); var rw=restart.OpenHome(); Jobs();
        var rd=(DumpView)Named<ContentControl>(rw,"DumpHost").Content!;
        Check(rd.Editor.Text=="keep through flush + retry" && restart.Store.Snapshot.Widgets.Single().Editor.Text=="outgoing after failure", "Restart lost either scoped draft");
        Render(rw,"scoped-dump"); restart.TryQuit(); Jobs();
    }

    using (var commandSession = new WorkspaceSession(Path.Combine(root,"commands")))
    {
        var kw = commandSession.OpenHome(); Jobs();
        Named<Button>(kw,"SpaceMenuButton").Focus(); kw.KeyTextInput("/"); Jobs();
        var search = Named<TextBox>(kw,"CommandBox");
        Check(Named<Border>(kw,"CommandOverlay").IsVisible && search.IsFocused && search.Text == "/", "Slash didn't focus a seeded command search");
        Render(kw,"command-search");
        kw.KeyTextInput("new"); kw.KeyPressQwerty(PhysicalKey.Enter,RawInputModifiers.None); kw.KeyReleaseQwerty(PhysicalKey.Enter,RawInputModifiers.None); Jobs();
        Check(Named<Border>(kw,"NamingPanel").IsVisible && Named<TextBox>(kw,"NameBox").IsFocused && !Named<Border>(kw,"CommandOverlay").IsVisible,
            "/new didn't reuse naming flow");
        kw.KeyTextInput("command niche"); kw.KeyPressQwerty(PhysicalKey.Enter,RawInputModifiers.None); kw.KeyReleaseQwerty(PhysicalKey.Enter,RawInputModifiers.None); Jobs();
        Check(commandSession.Store.Snapshot.Index.Miches.Count == 2 && Named<TextBlock>(kw,"SpaceTitle").Text == "command niche", "/new didn't create exactly one active Miche");
        Named<TextBox>(kw,"QueryBox").Focus(); kw.KeyTextInput("/new");
        Named<TextBox>(kw,"QueryBox").RaiseEvent(new KeyEventArgs {RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter}); Jobs();
        kw.KeyPressQwerty(PhysicalKey.Escape,RawInputModifiers.None); kw.KeyReleaseQwerty(PhysicalKey.Escape,RawInputModifiers.None); Jobs();
        Check(commandSession.Store.Snapshot.Index.Miches.Count == 2 && !Named<Border>(kw,"NamingPanel").IsVisible, "Cancelling /new created a Miche");
        commandSession.OpenDump(); Jobs(); var kd=(DumpView)Named<ContentControl>(kw,"DumpHost").Content!;
        var micheMenu=Named<Button>(kw,"SpaceMenuButton").ContextMenu!;
        micheMenu.Open(Named<Button>(kw,"SpaceMenuButton")); Jobs();
        commandSession.SaveEditor(kd); Jobs();
        Check(micheMenu.IsOpen && ReferenceEquals(micheMenu,Named<Button>(kw,"SpaceMenuButton").ContextMenu), "Draft autosave dismissed the Miche menu");
        micheMenu.Close(); Jobs();
        kd.Editor.Focus(); kw.KeyTextInput("draft /new"); Jobs();
        Check(kd.Editor.Text == "draft /new" && !Named<Border>(kw,"CommandOverlay").IsVisible, "Slash in Dump stole normal text input");
        Named<Button>(kw,"SpaceMenuButton").Focus(); kw.KeyTextInput("/"); Jobs(); search.Text="/unavailable";
        search.RaiseEvent(new KeyEventArgs {RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter}); Jobs();
        Check(Named<Border>(kw,"CommandOverlay").IsVisible && kd.Editor.Text == "draft /new", "Unknown command changed the desk or draft");
        kw.KeyPressQwerty(PhysicalKey.Escape,RawInputModifiers.None); kw.KeyReleaseQwerty(PhysicalKey.Escape,RawInputModifiers.None); Jobs();
        Check(!Named<Border>(kw,"CommandOverlay").IsVisible && Named<Button>(kw,"SpaceMenuButton").IsFocused, "Command Escape didn't close and restore focus");
        kw.RaiseEvent(new KeyEventArgs {RoutedEvent=InputElement.KeyDownEvent,Key=Key.OemQuestion,KeyModifiers=KeyModifiers.Meta}); kw.KeyTextInput("/"); Jobs();
        Check(!Named<Border>(kw,"CommandOverlay").IsVisible, "Modified slash opened command search");
        kw.RaiseEvent(new KeyEventArgs {RoutedEvent=InputElement.KeyUpEvent,Key=Key.OemQuestion,KeyModifiers=KeyModifiers.None});
        kw.KeyTextInput("/"); Jobs(); search.Text="/new";
        search.RaiseEvent(new KeyEventArgs {RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter}); Jobs();
        Named<TextBox>(kw,"NameBox").Text="another";
        Named<TextBox>(kw,"NameBox").RaiseEvent(new KeyEventArgs {RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter}); Jobs();
        Check(commandSession.Store.Snapshot.Widgets.Single().Editor.Text=="draft /new", "Creating from a populated desk lost its old draft");
        kw.ToggleVision(); Jobs(); var kv=Named<VisionView>(kw,"VisionOverlay"); kv.BeginText(new Point(50,50)); Jobs(); kw.KeyTextInput("vision /new"); Jobs();
        Check(kv.DraftEditor!.Text=="vision /new" && !Named<Border>(kw,"CommandOverlay").IsVisible, "Slash in Vision editor opened command search");
        kv.CommitDraft(); Jobs(); kw.KeyTextInput("/"); Jobs();
        Check(Named<Border>(kw,"CommandOverlay").IsVisible && !kv.IsVisible && commandSession.Store.Snapshot.VisionBoards.Single().Items.Single().Text=="vision /new", "Slash from Vision canvas lost the board or didn't open search");
        commandSession.TryQuit(); Jobs();
    }

    using (var chromeSession = new WorkspaceSession(Path.Combine(root,"chrome")))
    {
        var cw = chromeSession.OpenHome(); Jobs();
        Check(cw.SystemDecorations == SystemDecorations.BorderOnly && cw.ExtendClientAreaToDecorationsHint &&
            cw.ExtendClientAreaChromeHints == Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome && cw.CanResize,
            "Custom main header left native chrome or disabled resizing");
        var buttons = Named<WindowButtons>(cw,"MainWindowButtons");
        Press(buttons,"ExpandWindowButton"); Check(cw.WindowState == WindowState.Maximized,"Custom expand failed");
        Press(buttons,"ExpandWindowButton"); Check(cw.WindowState == WindowState.Normal,"Custom restore failed");
        Press(buttons,"MinimizeWindowButton"); Check(cw.WindowState == WindowState.Minimized,"Custom minimize failed");
        chromeSession.OpenHome(); Jobs(); Check(cw.WindowState == WindowState.Normal,"Reopening minimized home failed");
        var headerAt = Named<Border>(cw,"WindowHeader").TranslatePoint(new Point(400,22),cw)!.Value;
        cw.MouseDown(headerAt,MouseButton.Left); cw.MouseUp(headerAt,MouseButton.Left);
        cw.MouseDown(headerAt,MouseButton.Left); cw.MouseUp(headerAt,MouseButton.Left); Jobs();
        Check(cw.WindowState == WindowState.Maximized,"Blank header doubleclick didn't zoom");
        Press(buttons,"ExpandWindowButton");
        cw.KeyPressQwerty(PhysicalKey.F,RawInputModifiers.Meta|RawInputModifiers.Control);
        cw.KeyReleaseQwerty(PhysicalKey.F,RawInputModifiers.Meta|RawInputModifiers.Control); Jobs();
        Check(cw.WindowState == WindowState.FullScreen,"Native fullscreen shortcut failed");
        cw.KeyPressQwerty(PhysicalKey.Escape,RawInputModifiers.None); cw.KeyReleaseQwerty(PhysicalKey.Escape,RawInputModifiers.None); Jobs();
        Check(cw.WindowState == WindowState.Normal,"Escape didn't leave fullscreen");
        chromeSession.OpenDump(); Jobs();
        var cv = (DumpView)Named<ContentControl>(cw,"DumpHost").Content!; cv.Editor.Text="chrome draft";
        Check(!Named<WindowButtons>(cv,"FloatWindowButtons").IsVisible,"Docked card showed duplicate window buttons");
        Press(cv,"PopOutButton"); var cf=chromeSession.FloatingWindows[cv.WidgetId];
        Check(cf.SystemDecorations == SystemDecorations.BorderOnly && Named<WindowButtons>(cv,"FloatWindowButtons").IsVisible &&
            ReferenceEquals(cf.Content,cv),"Floating custom chrome rebuilt editor or left decorations");
        Press(buttons,"CloseWindowButton"); Check(!cw.IsVisible && cf.IsVisible,"Custom home close killed float");
        Press(Named<WindowButtons>(cv,"FloatWindowButtons"),"CloseWindowButton"); Jobs();
        Check(cw.IsVisible && chromeSession.FloatingWindows.Count==0 && cv.Editor.Text=="chrome draft","Custom float close lost draft or didn't redock");
        Press(cv,"PopOutButton"); cf=chromeSession.FloatingWindows[cv.WidgetId];
        cf.RaiseEvent(new KeyEventArgs { RoutedEvent=InputElement.KeyDownEvent, Key=Key.W, KeyModifiers=KeyModifiers.Meta }); Jobs();
        Check(chromeSession.FloatingWindows.Count==0 && cw.IsVisible,"Command W failed on floating window");
        cw.RaiseEvent(new KeyEventArgs { RoutedEvent=InputElement.KeyDownEvent, Key=Key.W, KeyModifiers=KeyModifiers.Meta }); Jobs();
        Check(chromeSession.IsQuitting,"Command W didn't safely close last home");
    }

    // The control identity, caret/selection and functional undo must survive host transfers.
    dump.Editor.Focus(); window.KeyTextInput("a multiline\ndraft"); Jobs();
    var draft = dump.Editor.Text;
    Check(dump.Editor.CanUndo, "Input didn't create an undo record");
    dump.Editor.CaretIndex = 4; dump.Editor.SelectionStart = 2; dump.Editor.SelectionEnd = 6;
    var editorBefore = dump.EditorSnapshot();
    Press(dump, "PopOutButton");
    var floating = session.FloatingWindows[dump.WidgetId];
    Check(ReferenceEquals(floating.Content, dump) && Named<ContentControl>(window, "DumpHost").Content is null, "Popout duplicated or retained two hosts");
    Check(dump.Editor.Text == draft && dump.Editor.CaretIndex == editorBefore.CaretIndex &&
          dump.Editor.SelectionStart == 2 && dump.Editor.SelectionEnd == 6 && dump.Editor.CanUndo, $"Popout lost editor state before={System.Text.Json.JsonSerializer.Serialize(editorBefore)} after={System.Text.Json.JsonSerializer.Serialize(dump.EditorSnapshot())} undo={dump.Editor.CanUndo}");
    session.PopOut(dump.WidgetId); Jobs();
    Check(session.FloatingWindows.Count == 1 && ReferenceEquals(floating, session.FloatingWindows[dump.WidgetId]), "Repeated popout duplicated a window");
    Render(floating, "dump-popout");
    Press(dump, "DockButton");
    Check(session.FloatingWindows.Count == 0 && ReferenceEquals(Named<ContentControl>(window, "DumpHost").Content, dump), "Dock didn't return same view");
    Check(dump.Editor.Text == draft && dump.Editor.SelectionStart == 2 && dump.Editor.SelectionEnd == 6 && dump.Editor.CanUndo, "Dock lost text/selection/undo");
    dump.Editor.Undo(); Check(dump.Editor.Text != draft, "Undo broke across transfer");
    dump.Editor.Redo(); Check(dump.Editor.Text == draft, "Redo broke across transfer");
    for (var cycle = 0; cycle < 3; cycle++) { Press(dump, "PopOutButton"); Press(dump, "DockButton"); }
    Check(ReferenceEquals(Named<ContentControl>(window, "DumpHost").Content, dump) && dump.Editor.Text == draft, "Repeated transfer changed draft/control");

    Press(dump, "PopOutButton"); floating = session.FloatingWindows[dump.WidgetId];
    Click(window, "home"); Click(window, "+ add Dump");
    var homeDump = (DumpView)Named<ContentControl>(window, "DumpHost").Content!;
    Check(homeDump.WidgetId != dump.WidgetId && dump.MicheId == studioId && floating.Title == "Dump · work", "Floating niche ownership changed");
    homeDump.Editor.Focus(); window.KeyTextInput("from home");
    homeDump.Editor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); Jobs();
    Check(Named<StackPanel>(dump, "CaptureList").GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "from home") && dump.Editor.Text == draft,
        "Shared note refresh destroyed foreign draft");
    dump.Editor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); Jobs();
    Check(session.Store.Snapshot.RootDump.Single(n => n.Text == draft!.Trim()).OriginMicheId == studioId,
        "Floating capture used home instead of originating niche");
    dump.Editor.Text = "survive home close"; dump.Editor.CaretIndex = 7;
    window.Close(); Jobs();
    Check(!window.IsVisible && floating.IsVisible && !session.IsQuitting, "Closing home killed floating session");
    Check(session.Store.Snapshot.Widgets.Single(w => w.Id == dump.WidgetId).Editor.Text == "survive home close", "Home close didn't save draft");
    bool secondRejected = false;
    try { using var second = new WorkspaceStore(root); } catch (IOException) { secondRejected = true; }
    Check(secondRejected, "Hidden home released lifetime writer lock");
    Press(dump, "HomeButton"); Check(window.IsVisible && ReferenceEquals(window, session.OpenHome()), "Home affordance didn't reopen window");
    floating.Close(); Jobs();
    Check(session.FloatingWindows.Count == 0 && window.IsVisible && session.Store.Snapshot.Index.ActiveMicheId == studioId &&
        ReferenceEquals(Named<ContentControl>(window, "DumpHost").Content, dump), "Window close didn't redock in originating niche");
    Click(window, "hide"); Click(window, "+ add Dump");
    Check(ReferenceEquals(Named<ContentControl>(window, "DumpHost").Content, dump) && dump.Editor.Text == "survive home close", "Hide lost unsent draft");

    // Failed writes must retain the host and draft, including an attempted Quit.
    Press(dump, "PopOutButton"); floating = session.FloatingWindows[dump.WidgetId];
    File.Delete(session.Store.FilePath + ".bak"); Directory.CreateDirectory(session.Store.FilePath + ".bak");
    var fileBefore = File.ReadAllText(session.Store.FilePath);
    floating.Close(); Jobs();
    Check(floating.IsVisible && ReferenceEquals(floating.Content, dump) && session.FloatingWindows.Count == 1 &&
        session.Store.Snapshot.Placements.Single(p => p.WidgetInstanceId == dump.WidgetId).Mode == "floating", "Failed dock lost host");
    Check(!session.TryQuit() && !session.IsQuitting && floating.IsVisible && dump.Editor.Text == "survive home close" &&
        Named<TextBlock>(dump, "Feedback").IsVisible && File.ReadAllText(session.Store.FilePath) == fileBefore, "Failed Quit discarded editor or durable state");
    Directory.Delete(session.Store.FilePath + ".bak");

    Click(window, "manage · recently deleted"); Click(window, "move to recently deleted");
    Check(!session.FloatingWindows.ContainsKey(dump.WidgetId) && session.Store.Snapshot.Trash.Single().Miche.Id == studioId, "Deleting niche left float open");
    Click(window, "restore"); Click(window, "done");
    Check(session.FloatingWindows.ContainsKey(dump.WidgetId) && ReferenceEquals(session.FloatingWindows[dump.WidgetId].Content, dump) &&
        dump.Editor.Text == "survive home close", "Restore lost floating editor");
    floating = session.FloatingWindows[dump.WidgetId]; floating.Width = 610; floating.Height = 390; floating.Position = new PixelPoint(35,45);
    dump.Editor.CaretIndex = 3; dump.Editor.SelectionStart = 1; dump.Editor.SelectionEnd = 4;
    Check(session.TryQuit(), "Quit didn't save"); Jobs();
    session = new WorkspaceSession(root); window = session.OpenHome(); Jobs();
    var restored = session.ViewFor(dump.WidgetId);
    Check(session.FloatingWindows.Count == 1 && restored.Editor.Text == "survive home close" && restored.Editor.SelectionStart == 1 &&
        restored.Editor.SelectionEnd == 4 && restored.Editor.CaretIndex == 3, "Restart lost floating draft/selection");
    Check(session.FloatingWindows[dump.WidgetId].Width == 610 && session.FloatingWindows[dump.WidgetId].Height == 390,
        "Restart lost saved window dimensions");
    Press(restored, "DockButton"); Click(window, "home");
    Check(Named<ContentControl>(window, "DumpHost").Content is DumpView shared &&
        Named<StackPanel>(shared, "CaptureList").GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "from home"), "Restart lost shared captures");
    var homeEditor = session.ViewFor(homeDump.WidgetId).Editor;
    homeEditor.Focus(); window.KeyTextInput("debounced home draft"); Jobs();
    using (var wait = new System.Threading.CancellationTokenSource(750)) Dispatcher.UIThread.MainLoop(wait.Token); Jobs();
    Check(session.Store.Snapshot.Widgets.Single(w => w.Id == homeDump.WidgetId).Editor.Text == "debounced home draft", $"Idle draft wasn't autosaved live={homeEditor.Text} saved={session.Store.Snapshot.Widgets.Single(w => w.Id == homeDump.WidgetId).Editor.Text}");
    homeEditor.CaretIndex = 5; homeEditor.SelectionStart = 2; homeEditor.SelectionEnd = 8;
    Check(session.TryQuit(), "Docked quit failed"); Jobs();
    session = new WorkspaceSession(root); window = session.OpenHome(); Jobs();
    var reopenedHome = session.ViewFor(homeDump.WidgetId).Editor;
    Check(reopenedHome.Text == "debounced home draft" && reopenedHome.CaretIndex == 5 && reopenedHome.SelectionStart == 2 && reopenedHome.SelectionEnd == 8,
        "Restart lost docked editor state");
    // Every live float saves independently, even when home is on another niche.
    Press(session.ViewFor(homeDump.WidgetId), "PopOutButton");
    var workView = session.ViewFor(dump.WidgetId); session.PopOut(workView.WidgetId); Jobs();
    reopenedHome.Text = "home separate draft"; workView.Editor.Text = "work separate draft";
    session.Store.Activate(homeId); Jobs();
    Check(session.FloatingWindows.Count == 2 && session.TryQuit(), "Multi-float quit failed"); Jobs();
    session = new WorkspaceSession(root); window = session.OpenHome(); Jobs();
    Check(session.FloatingWindows.Count == 2 && session.ViewFor(homeDump.WidgetId).Editor.Text == "home separate draft" &&
        session.ViewFor(dump.WidgetId).Editor.Text == "work separate draft", "Multi-float quit/restart mixed or lost drafts");
    session.Dock(dump.WidgetId); session.Dock(homeDump.WidgetId); Jobs();
    // Closing the final home window intentionally quits and releases the store.
    window.Close(); Jobs(); Check(session.IsQuitting, "Last home close didn't quit");
    using var unlocked = new WorkspaceStore(root);
    Check(unlocked.Snapshot.Widgets.Count == 2, "Quit didn't release writer or lost instances");
    // New page and shared-table contracts: persistence, failure boundaries and routed UI.
    using(var pageSession=new WorkspaceSession(Path.Combine(root,"pages-contract")))
    {
        var pw=pageSession.OpenHome();Jobs();pw.OpenCommands();Named<TextBox>(pw,"CommandBox").Text="/table";Named<TextBox>(pw,"CommandBox").RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter});Jobs();
        var page=pageSession.Pages.Snapshot.Pages.Single();var view=pageSession.PageViewFor(page.Id);
        Check(page.Blocks.Single().Kind=="table"&&Named<Miche.Mac.Controls.PageDesk>(pw,"PagesDesk").Children.Contains(view),"Standalone table was not a docked page");
        var moveAt=view.MoveGrip.TranslatePoint(new Point(6,6),pw)!.Value;
        pw.MouseDown(moveAt,MouseButton.Left);pw.MouseMove(moveAt+new Vector(60,30));pw.MouseUp(moveAt+new Vector(60,30),MouseButton.Left);Jobs();
        Check(pageSession.Pages.Get(page.Id).Left==page.Left+60&&pageSession.Pages.Get(page.Id).Top==page.Top+30,"Page drag did not save release position");
        var resizeAt=view.ResizeGrip.TranslatePoint(new Point(5,5),pw)!.Value;
        pw.MouseDown(resizeAt,MouseButton.Left);pw.MouseMove(resizeAt+new Vector(40,20));pw.MouseUp(resizeAt+new Vector(40,20),MouseButton.Left);Jobs();
        Check(pageSession.Pages.Get(page.Id).Width==page.Width+40&&pageSession.Pages.Get(page.Id).Height==page.Height+20,"Page resize did not save release dimensions");
        view.TitleEditor.Text="calendar";Jobs();Check(pageSession.Pages.Get(page.Id).Title=="calendar","Page title did not save before WebKit loads");
        pageSession.PopoutPage(page.Id);Jobs();Check(pageSession.Pages.Get(page.Id).Floating,"Page did not pop out");
        pageSession.DockPage(page.Id);Jobs();Check(!pageSession.Pages.Get(page.Id).Floating&&Named<Miche.Mac.Controls.PageDesk>(pw,"PagesDesk").Children.Contains(view),"Page did not dock the same view");
        var child=pageSession.Pages.Create(page.MicheId,parent:page.Id);Check(pageSession.Pages.Get(child).ParentId==page.Id,"Nested page identity missing");
        try{pageSession.Pages.Nest(page.Id,child);throw new Exception("Cycle accepted");}catch(InvalidDataException){checks++;}
        Check(pageSession.Pages.Get(page.Id).ParentId is null,"Rejected nesting changed state");
        pageSession.Pages.Delete(child,true);pageSession.Pages.Delete(child,false);Check(pageSession.Pages.Get(child).DeletedAt is null,"Page deletion was not recoverable");
        var content=pageSession.Pages.Get(page.Id).Blocks;content[0].Cells[0][0].Add(new Miche.Mac.Models.PageRun{Text="Launch",Bold=true,Italic=true,Boxed=true,Size=24});
        pageSession.Pages.SaveContent(page.Id,"calendar",content);var reopened=new PageRepository(pageSession.Store.DirectoryPath);
        var run=reopened.Get(page.Id).Blocks[0].Cells[0][0][0];Check(run.Bold&&run.Italic&&run.Boxed&&run.Size==24&&run.Text=="Launch","Formatted text did not round-trip");
        var bytes=File.ReadAllBytes(pageSession.Pages.FilePath);File.Delete(pageSession.Pages.FilePath+".bak");Directory.CreateDirectory(pageSession.Pages.FilePath+".bak");
        try{pageSession.Pages.SaveTitle(page.Id,"must fail");throw new Exception("Save failure missing");}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){checks++;}
        Check(pageSession.Pages.Get(page.Id).Title=="calendar"&&File.ReadAllBytes(pageSession.Pages.FilePath).SequenceEqual(bytes),"Failed page write changed memory or disk");Directory.Delete(pageSession.Pages.FilePath+".bak");
        var table=TableContent.Create();TableContent.Dimensions(table);table.CellColors["1:1"]=TableContent.Palette[1].Color;
        TableContent.Insert(table,true,0);Check(table.CellColors.ContainsKey("2:1"),"Insert shifted cell color incorrectly");TableContent.Remove(table,false,0);Check(table.CellColors.ContainsKey("2:0"),"Delete shifted cell color incorrectly");
        var tw=new TableView(table,_=>true);tw.SelectCells(new[]{(0,0),(1,1)});tw.ResizeSelection(false,160);tw.ColorSelection(TableContent.Palette[3].Color);
        Check(tw.Snapshot.ColumnWidths.All(w=>w==160)&&tw.Snapshot.CellColors["0:0"]==TableContent.Palette[3].Color,"Multi-cell size/color failed");
        pw.ToggleVision();Jobs();var vision=Named<VisionView>(pw,"VisionOverlay");Check(vision.AddTable(new Point(40,40)),"Vision table creation failed");
        var vi=pageSession.Store.Snapshot.VisionBoards.Single().Items.Single();vision.SelectObject(vi.Id);var copy=vision.CopySelection();Check(copy is not null&&vision.PasteSelection(copy),"Vision table did not copy/paste");
        var tables=pageSession.Store.Snapshot.VisionBoards.Single().Items;Check(tables.Count==2&&tables.All(i=>i.Table?.Cells.Count==3),"Vision copied table content was lost");
        Jobs();pw.UpdateLayout();Check(pw.GetVisualDescendants().OfType<TableView>().Count()==2&&pw.GetVisualDescendants().OfType<TableView>().All(t=>t.Bounds.Width>0&&t.GetVisualDescendants().OfType<TextBox>().Count()==9),"Vision table cells were not laid out");Render(pw,"pages-and-tables");pageSession.TryQuit();Jobs();
    }
    using(var cs=new WorkspaceSession(Path.Combine(root,"calendar-controls")))
    {
        var home=cs.OpenHome();Jobs();var owner=cs.Store.Snapshot.Index.RootMicheId;
        var viet=cs.Store.Create("tieng viet");cs.Store.SetMicheNoteColor(viet,WorkspaceStore.NoteColors[1]);
        var calendarMeili=cs.Store.Create("thedailymeili");cs.Store.SetMicheNoteColor(calendarMeili,WorkspaceStore.NoteColors[0]);cs.Store.Activate(owner);Jobs();
        home.ToggleVision();Jobs();var vision=Named<VisionView>(home,"VisionOverlay");
        vision.BeginText(new Point(80,80));Jobs();vision.DraftEditor!.Text="/@tieng viet -- viet homework + flashcards";
        Check(vision.CommitDraft(),"Miche mention did not commit");Jobs();
        var source=cs.Store.Snapshot.VisionBoards.Single().Items.Single();var calendarShared=source.CalendarItemId!.Value;
        Check(source.LinkedMicheId==viet&&source.Text=="viet homework + flashcards"&&cs.Store.Snapshot.Calendar.Items.Single().Date is null,"Miche mention did not become a staged connected note");
        vision.BeginText(new Point(80,200));Jobs();vision.DraftEditor!.Text="/@thedailymeili";vision.DraftEditor.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter});Jobs();
        Check(vision.DraftEditor is not null&&vision.DraftEditor.Text=="","Mention-only Enter did not leave room for the note body");vision.DraftEditor!.Text="create first mail club";vision.CommitDraft();Jobs();
        Check(cs.Store.Snapshot.Calendar.Items.Count==2,"Second Miche note replaced the first");
        vision.BoardCanvas.Focus();home.KeyPressQwerty(PhysicalKey.C,RawInputModifiers.None);home.KeyReleaseQwerty(PhysicalKey.C,RawInputModifiers.None);Jobs();
        var cw=cs.CalendarWindow!;var cv=cw.Calendar;Check(cw.IsVisible&&cv.DayCards.Count==DateTime.DaysInMonth(DateTime.Today.Year,DateTime.Today.Month),"C did not open canonical month");
        Check(cv.DayCards.Values.All(card=>card.Bounds.Width>=80&&card.Bounds.Width<=150),"Month grid does not fit seven readable day columns");
        cs.OpenCalendar();Jobs();Check(ReferenceEquals(cw,cs.CalendarWindow),"Canonical calendar opened duplicate windows");
        var today=DateTime.Today.ToString("yyyy-MM-dd");var month=today[..7];
        var drag=cv.Editor.ObjectControls[calendarShared].TranslatePoint(new Point(18,45),cw)!.Value;
        var drop=cv.DayCards[today].TranslatePoint(new Point(34,55),cw)!.Value;int calendarCommits=0;void CountCommit()=>calendarCommits++;cs.Store.Changed+=CountCommit;
        cw.MouseDown(drag,MouseButton.Left);cw.MouseMove(drop);cw.MouseUp(drop,MouseButton.Left);Jobs();cs.Store.Changed-=CountCommit;
        Check(cs.Store.Snapshot.Calendar.Items.Single(i=>i.Id==calendarShared).Date==today&&calendarCommits==1,"Margin drag did not assign the same object with one commit");
        Check(cv.ShowDate(today),"Could not open day canvas");Jobs();Check(cv.Editor.ObjectControls.ContainsKey(calendarShared),"Day canvas did not use canonical object");
        var current=cs.Store.Snapshot.Calendar.Items.Single(i=>i.Id==calendarShared).Content;
        cv.Editor.BeginText(new Point(current.Left,current.Top),current);Jobs();cv.Editor.DraftEditor!.Text="viet homework + ten flashcards";cv.Editor.CommitDraft();Jobs();
        Check(cs.Store.Snapshot.VisionBoards.Single().Items.Single(i=>i.Id==source.Id).Text=="viet homework + ten flashcards","Day edit diverged from Vision");
        cs.Store.SaveCalendarItems(month,today,new[]{new Miche.Mac.Models.VisionItem{Text="meal planned: salmon",Left=24,Top=140,Width=260,Height=40,FontSize=18},new Miche.Mac.Models.VisionItem{Text="important: interview",Left=24,Top=210,Width=260,Height=40,FontSize=18}});Jobs();
        Render(cw,"calendar-day");
        cv.ShowMargin();Jobs();Render(cw,"calendar-month");
        Check(cv.SetView("vertical")&&cv.DayCards.Count==DateTime.DaysInMonth(DateTime.Today.Year,DateTime.Today.Month),"Vertical view lost day cards");Jobs();
        cv.MonthScroll.Offset=new Vector(0,Math.Max(0,(DateTime.Today.Day-2)*156));Jobs();Render(cw,"calendar-vertical");
        Check(cv.SetView("month")&&cs.Store.Snapshot.Calendar.Items.Count==4,"View switching copied or erased calendar objects");
        cv.ShowDate(today);Jobs();
        Check(cv.Editor.ImportImage(Path.Combine(AppContext.BaseDirectory,"fixtures","vision.png"),new Point(24,300)),"Calendar image import failed");Jobs();
        var calendarImage=cs.Store.Snapshot.Calendar.Items.Single(i=>i.Content.Kind=="image");
        Check(calendarImage.OwnerMicheId==owner&&calendarImage.Date==today&&File.Exists(cs.Store.VisionAssetPath(owner,calendarImage.Content.FileName!)),"Calendar image lost asset ownership or day assignment");
        cv.Editor.SelectObject(calendarImage.Id);var imageCopy=cv.Editor.CopySelection();Check(imageCopy is not null&&cv.Editor.PasteSelection(imageCopy),"Calendar image copy/paste failed");Jobs();
        Check(cs.Store.Snapshot.Calendar.Items.Count(i=>i.Content.Kind=="image")==2,"Calendar image paste replaced its original");
        cs.Store.Rename(viet,"tiếng Việt");vision.BeginText(new Point(80,320));Jobs();vision.DraftEditor!.Text="/@tieng viet -- bring yesterday forward";vision.CommitDraft();Jobs();
        Check(cs.Store.Snapshot.VisionBoards.Single().Items.Any(i=>i.LinkedMicheId==viet&&i.Text=="bring yesterday forward"),"Accent-free mention did not find the existing Miche");
        cs.Store.Rename(calendarMeili,"@thedailymeili");vision.BeginText(new Point(80,460));Jobs();vision.DraftEditor!.Text="/@thedailymeili -- plan the next issue";vision.CommitDraft();Jobs();
        Check(cs.Store.Snapshot.VisionBoards.Single().Items.Any(i=>i.LinkedMicheId==calendarMeili&&i.Text=="plan the next issue"),"Leading-at Miche name required a double-at command");
        Check(cv.ChangeMonth(1)&&cv.Editor.ObjectControls.Count==0&&cv.ChangeMonth(-1),"Month staging leaked into another month");Jobs();
        var count=cs.Store.Snapshot.Calendar.Items.Count;cv.Editor.BeginText(new Point(80,200));Jobs();cv.Editor.DraftEditor!.Text="/@tieng viet";
        Check(cv.ShowDate(today)&&cv.Editor.DraftEditor is null&&cs.Store.Snapshot.Calendar.Items.Count==count,"Leaving a mention-only draft moved it onto a different day");
        home.HideVisionForNavigation();home.OpenCommands();Named<TextBox>(home,"CommandBox").Text="/calendar";Named<TextBox>(home,"CommandBox").RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter});Jobs();
        Check(Named<ContentControl>(home,"CalendarHost").Content is CalendarWidgetView&&cs.Store.Snapshot.Calendar.VisibleIn.Contains(owner),"/calendar did not add the today widget");Render(home,"calendar-widget");
        cs.Store.Activate(calendarMeili);cs.OpenCalendarWidget();Jobs();Check(cs.Store.Snapshot.Calendar.VisibleIn.Count==2&&cs.Store.Snapshot.Calendar.Items.Count==count,"Calendar in another Miche did not share today’s collection");
        cv.ShowDate(today);cv.Editor.BeginText(new Point(24,290));Jobs();cv.Editor.DraftEditor!.Text="a thought to keep for tomorrow";
        Check(cs.TryQuit(),"Calendar draft blocked normal quit");Jobs();
        using var persisted=new WorkspaceStore(cs.Store.DirectoryPath);Check(persisted.Snapshot.Calendar.Items.Any(i=>i.Date==today&&i.Content.Text=="a thought to keep for tomorrow"),"Calendar quit discarded a day draft");
    }
    using(var cs=new WorkspaceSession(Path.Combine(root,"calendar-writing")))
    {
        cs.OpenHome();Jobs();cs.OpenCalendar();Jobs();var cw=cs.CalendarWindow!;var cv=cw.Calendar;var today=DateTime.Today.ToString("yyyy-MM-dd");
        var point=cv.DayCards[today].TranslatePoint(new Point(20,90),cw)!.Value;
        cw.MouseDown(point,MouseButton.Left);cw.MouseUp(point,MouseButton.Left);Jobs();
        Check(cv.InlineEditor?.IsFocused==true,"Clicking a day did not focus writing inside the tile");
        cw.KeyTextInput("meal planned: salmon");Jobs();
        cw.KeyPressQwerty(PhysicalKey.Enter,RawInputModifiers.None);cw.KeyReleaseQwerty(PhysicalKey.Enter,RawInputModifiers.None);Jobs();
        var meal=cs.Store.Snapshot.Calendar.Items.Single();
        Check(meal.Date==today&&meal.Content.Text=="meal planned: salmon"&&cv.InlineEditor?.IsFocused==true,"Enter did not save the note and keep writing below it");
        cw.KeyTextInput("important: interview");Jobs();var editor=cv.InlineEditor;
        cs.Store.Rename(cs.Store.Snapshot.Index.RootMicheId,"home, continued");cw.Width-=40;Jobs();
        Check(ReferenceEquals(editor,cv.InlineEditor)&&editor!.Text=="important: interview"&&editor.IsFocused,"Refresh or resize discarded the inline caret/draft");
        Check(cv.CommitInline(),"Inline note could not commit");Jobs();
        var interview=cs.Store.Snapshot.Calendar.Items.Single(i=>i.Id!=meal.Id);
        Check(interview.Content.Top>meal.Content.Top+meal.Content.Height&&cs.Store.Snapshot.Calendar.Items.Single(i=>i.Id==meal.Id).Content.Text==meal.Content.Text,"Continuation overwrote or overlapped the earlier note");
        var previous=cv.NotePreviews[meal.Id].TranslatePoint(new Point(8,8),cw)!.Value;
        cw.MouseDown(previous,MouseButton.Left);cw.MouseUp(previous,MouseButton.Left);Jobs();
        Check(cv.InlineEditor is {Text:""},"One click on a filled card did not continue underneath its existing text");
        cw.KeyTextInput("keep yesterday’s thought");Check(cv.CommitInline(),"Filled-card continuation failed");Jobs();
        Check(cs.Store.Snapshot.Calendar.Items.Count==3&&cs.Store.Snapshot.Calendar.Items.Single(i=>i.Id==meal.Id).Content.Text==meal.Content.Text,"Click-to-continue changed prior writing");
        cv.ShowMargin();Jobs();Check(cv.Editor.AddShape("ellipse",new Point(40,40),180,100),"Could not draw a loose oval");Jobs();
        var shape=cs.Store.Snapshot.Calendar.Items.Single(i=>i.Content.Kind=="ellipse");
        Check(cv.Editor.ObjectControls[shape.Id].GetVisualDescendants().Any(c=>c.GetType().Name=="VisionPrimitive"),"Loose shape rendered as a word");
        var from=cv.Editor.ObjectControls[shape.Id].TranslatePoint(new Point(80,5),cw)!.Value;
        var onto=cv.NotePreviews[meal.Id].TranslatePoint(new Point(10,10),cw)!.Value;var commits=0;void Count()=>commits++;cs.Store.Changed+=Count;
        cw.MouseDown(from,MouseButton.Left);cw.MouseMove(onto);cw.MouseUp(onto,MouseButton.Left);Jobs();cs.Store.Changed-=Count;
        var decorated=cs.Store.Snapshot.Calendar.Items.Single(i=>i.Id==meal.Id);
        Check(decorated.Content.NoteShape=="ellipse"&&cs.Store.Snapshot.Calendar.Items.Single(i=>i.Id==shape.Id).Content.DeletedAt is not null&&commits==1,"Dropping an oval onto a note did not combine them atomically");
        Check(cs.Store.Snapshot.Calendar.Items.Single(i=>i.Id==interview.Id).Content.Top>=decorated.Content.Top+decorated.Content.Height,"Wrapping an earlier note overlapped its continuation");
        Check(cv.NotePreviews[meal.Id].GetVisualDescendants().Any(c=>c.GetType().Name=="VisionPrimitive"),"Calendar did not draw the oval around the note");
        cv.ShowDate(today);Jobs();Check(cv.Editor.SetNoteShape(interview.Id,"rounded-rectangle"),"Could not frame selected note");Jobs();
        Check(cv.Editor.AddShape("rounded-rectangle",new Point(24,300),160,90),"Could not draw a frame in the day editor");Jobs();
        var looseFrame=cs.Store.Snapshot.Calendar.Items.Single(i=>i.Content.Kind=="rounded-rectangle");
        var frameAt=cv.Editor.ObjectControls[looseFrame.Id].TranslatePoint(new Point(70,5),cw)!.Value;
        var textAt=cv.Editor.ObjectControls[interview.Id].TranslatePoint(new Point(10,10),cw)!.Value;
        cw.MouseDown(frameAt,MouseButton.Left);cw.MouseMove(textAt);cw.MouseUp(textAt,MouseButton.Left);Jobs();
        Check(cs.Store.Snapshot.Calendar.Items.Single(i=>i.Id==looseFrame.Id).Content.DeletedAt is not null&&cs.Store.Snapshot.Calendar.Items.Single(i=>i.Id==interview.Id).Content.NoteShape=="rounded-rectangle","Dragging the edge of a shape onto canvas text did not attach it");
        Check(cv.BeginInline(today,cs.Store.Snapshot.Calendar.Items.Single(i=>i.Id==meal.Id)),"Could not edit decorated note");Jobs();cw.KeyTextInput(" + greens");Check(cv.CommitInline(),"Decorated note edit failed");Jobs();
        Check(cs.Store.Snapshot.Calendar.Items.Single(i=>i.Id==meal.Id).Content.NoteShape=="ellipse"&&cs.Store.Snapshot.Calendar.Items.Single(i=>i.Id==meal.Id).Content.Text=="meal planned: salmon + greens","Editing broke the combined shape/text component");
        cv.BeginInline(today);Jobs();cw.KeyTextInput("save me after a failed write");
        var before=File.ReadAllBytes(cs.Store.FilePath);File.Delete(cs.Store.FilePath+".bak");Directory.CreateDirectory(cs.Store.FilePath+".bak");
        Check(!cv.SetView("vertical")&&cv.InlineEditor?.Text=="save me after a failed write"&&File.ReadAllBytes(cs.Store.FilePath).SequenceEqual(before),"Failed save lost inline writing or changed views");Directory.Delete(cs.Store.FilePath+".bak");
        Check(cv.SetView("vertical"),"Recovered writing could not switch views");Jobs();cv.MonthScroll.Offset=new Vector(0,Math.Max(0,(DateTime.Today.Day-1)*156));Jobs();
        cv.BeginInline(today);Jobs();Render(cw,"calendar-writing");cv.CommitInline();
        var linked=cs.Store.Create("tiếng Việt");cv.BeginInline(today);Jobs();cw.KeyTextInput("/@tieng viet -- flashcards after lunch");
        Check(cv.CommitInline()&&cs.Store.Snapshot.Calendar.Items.Any(i=>i.Content.LinkedMicheId==linked&&i.Content.Text=="flashcards after lunch"),"Inline Miche mention lost its color/link");Jobs();
        cv.BeginInline(today);Jobs();cw.KeyTextInput("tomorrow starts here");Check(cs.TryQuit(),"Quit did not save direct tile writing");Jobs();
        using var reopened=new WorkspaceStore(cs.Store.DirectoryPath);
        Check(reopened.Snapshot.Calendar.Items.Any(i=>i.Content.Text=="tomorrow starts here")&&reopened.Snapshot.Calendar.Items.Single(i=>i.Id==meal.Id).Content.NoteShape=="ellipse","Restart lost direct writing or its attached shape");
    }
    using(var colors=new WorkspaceSession(Path.Combine(root,"miche-colors")))
    {
        var rootMiche=colors.Store.Snapshot.Index.RootMicheId;colors.Store.SetMicheNoteColor(rootMiche,WorkspaceStore.NoteColors[0]);
        var hw=colors.OpenHome();Jobs();var button=Named<Button>(hw,"ColorPickerButton");
        var picker=hw.GetLogicalDescendants().OfType<Avalonia.Controls.Primitives.Popup>().Single(p=>p.Name=="MicheColorPicker");
        Button Choice(string color)=>picker.Child!.GetVisualDescendants().OfType<Button>().Single(b=>b.Tag as string==color);
        void Pick(string color){if(!picker.IsOpen)Press(hw,"ColorPickerButton");Choice(color).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Jobs();}
        string HomeTint()=>((Avalonia.Media.LinearGradientBrush)hw.Background!).GradientStops[0].Color.ToString();
        Check(HomeTint()=="#ff422d36","An existing Miche color did not tint its home on opening");
        var date=Named<TextBlock>(hw,"DateLabel");var iconAt=button.TranslatePoint(new Point(15,14),hw)!.Value;
        Check(button.Bounds.Width>=28&&button.TranslatePoint(default,hw)!.Value.X>=date.TranslatePoint(new Point(date.Bounds.Width,0),hw)!.Value.X,"Color icon is not beside the date");
        hw.MouseDown(iconAt,MouseButton.Left);hw.MouseUp(iconAt,MouseButton.Left);Jobs();
        Check(picker.IsOpen&&picker.Child!.GetVisualDescendants().OfType<Button>().Count(b=>b.Tag is string)==6,"Footer palette icon did not open the six color choices");
        Pick(WorkspaceStore.NoteColors[2]);
        Check(colors.Store.Snapshot.Index.Miches.Single(m=>m.Id==rootMiche).NoteColor==WorkspaceStore.NoteColors[2]&&HomeTint()=="#ff2d3b4b"&&!picker.IsOpen,"Choosing blue did not immediately change and save the current Miche");
        Check(((Avalonia.Media.ISolidColorBrush)date.Foreground!).Color.ToString()=="#ffbccedc","Date accent did not match the Miche color");Render(hw,"miche-color-blue");
        var garden=colors.Store.Create("garden");Jobs();Pick(WorkspaceStore.NoteColors[4]);
        Check(HomeTint()=="#ff483a27"&&colors.Store.Snapshot.Index.Miches.Single(m=>m.Id==rootMiche).NoteColor==WorkspaceStore.NoteColors[2],"Changing another Miche recolored the previous one");
        colors.Store.Activate(rootMiche);Jobs();Check(HomeTint()=="#ff2d3b4b","Switching Miches did not restore their own colors");
        var menu=Named<Button>(hw,"SpaceMenuButton").ContextMenu!;menu.Open(Named<Button>(hw,"SpaceMenuButton"));Jobs();
        menu.Items.OfType<MenuItem>().Single(i=>i.Header as string=="Miche color…").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Jobs();
        Check(picker.IsOpen,"Miche menu did not use the working color picker");Pick(WorkspaceStore.NoteColors[3]);Check(HomeTint()=="#ff3b2e48","Miche menu color change did not tint home");
        Press(hw,"ColorPickerButton");colors.Store.Activate(garden);Jobs();Check(!picker.IsOpen,"Color picker stayed open for the wrong Miche after switching");
        colors.OpenCalendarWidget();Jobs();var today=DateTime.Today.ToString("yyyy-MM-dd");
        colors.Store.SaveCalendarItems(today[..7],today,new[]{new Miche.Mac.Models.VisionItem{Text="a garden thought",LinkedMicheId=garden,FontSize=18}});Jobs();
        var widget=(CalendarWidgetView)Named<ContentControl>(hw,"CalendarHost").Content!;
        Pick(WorkspaceStore.NoteColors[5]);
        Check(widget.GetVisualDescendants().OfType<Border>().Any(b=>b.Background is Avalonia.Media.ISolidColorBrush brush&&brush.Color.ToString()=="#ffbfd8d1"),"Changing a Miche color did not update its existing calendar bubble");
        var bytes=File.ReadAllBytes(colors.Store.FilePath);var tint=HomeTint();File.Delete(colors.Store.FilePath+".bak");Directory.CreateDirectory(colors.Store.FilePath+".bak");
        Pick(WorkspaceStore.NoteColors[1]);
        Check(picker.IsOpen&&HomeTint()==tint&&File.ReadAllBytes(colors.Store.FilePath).SequenceEqual(bytes),"Failed color save changed the UI/disk or closed its picker");Directory.Delete(colors.Store.FilePath+".bak");
        Pick(WorkspaceStore.NoteColors[1]);Check(HomeTint()=="#ff344030","Color picker could not recover after a failed save");
        hw.Width=480;Jobs();var icon=button.TranslatePoint(default,hw)!.Value;Check(icon.X>=0&&icon.Y>=0&&icon.X+button.Bounds.Width<=hw.Bounds.Width&&icon.Y+button.Bounds.Height<=hw.Bounds.Height,"Color icon is clipped at minimum home size");Render(hw,"miche-color-sage");
        Check(colors.TryQuit(),"Color-changing workspace did not quit normally");Jobs();
        using var reopened=new WorkspaceSession(colors.Store.DirectoryPath);var rh=reopened.OpenHome();Jobs();
        Check(reopened.Store.Snapshot.Index.Miches.Single(m=>m.Id==rootMiche).NoteColor==WorkspaceStore.NoteColors[3]&&reopened.Store.Snapshot.Index.Miches.Single(m=>m.Id==garden).NoteColor==WorkspaceStore.NoteColors[1]&&((Avalonia.Media.LinearGradientBrush)rh.Background!).GradientStops[0].Color.ToString()=="#ff344030","Miche colors did not survive restart");reopened.TryQuit();Jobs();
    }
    using(var graph=new WorkspaceSession(Path.Combine(root,"vision-connections-ui")))
    {
        var gw=graph.OpenHome();Jobs();var m=graph.Store.Snapshot.Index.ActiveMicheId;
        var a=new Miche.Mac.Models.VisionItem{Text="A thought",NoteShape="ellipse",Left=100,Top=100,Width=210,Height=80};
        var b=new Miche.Mac.Models.VisionItem{Text="Downstream",RoundedFrame=true,Left=480,Top=300,Width=160,Height=70};
        var c=new Miche.Mac.Models.VisionItem{Text="Another branch",RoundedFrame=true,Left=130,Top=500,Width=170,Height=70};
        graph.Store.SaveVisionItems(m,new[]{a,b,c});gw.ToggleVision();Jobs();var v=Named<VisionView>(gw,"VisionOverlay");
        Check(v.BeginConnection(a.Id),"Could not arm connection");Jobs();var click=v.ObjectControls[b.Id].TranslatePoint(new Point(40,35),gw)!.Value;
        gw.MouseDown(click,MouseButton.Left);gw.MouseUp(click,MouseButton.Left);Jobs();
        Check(v.ConnectionSourceId is null&&v.ConnectionControls.Count==1&&graph.Store.Snapshot.VisionBoards.Single().Items.Single(i=>i.Id==a.Id).DownstreamIds.SequenceEqual(new[]{b.Id}),"Routed downstream click did not save connection");
        Check(v.ConnectObjects(a.Id,c.Id)&&v.ConnectionControls.Count==2,"One thought could not branch");Jobs();
        var before=File.ReadAllBytes(graph.Store.FilePath);var curve=v.ConnectionControls.Single(l=>l.TargetId==b.Id).Curve;
        var at=v.ObjectControls[a.Id].TranslatePoint(new Point(40,35),gw)!.Value;
        gw.MouseDown(at,MouseButton.Left);gw.MouseMove(at+new Vector(40,20));Jobs();
        Check(v.ConnectionControls.Single(l=>l.TargetId==b.Id).Curve.Start!=curve.Start&&File.ReadAllBytes(graph.Store.FilePath).SequenceEqual(before),"Connection did not follow the unsaved drag preview");
        gw.MouseUp(at+new Vector(40,20),MouseButton.Left);Jobs();
        Check(graph.Store.Snapshot.VisionBoards.Single().Items.Single(i=>i.Id==a.Id).Left==140&&v.ConnectionControls.Count==2,"Release lost connection or object position");
        v.SelectObject(a.Id);v.RefreshBoard();Jobs();var port=v.ObjectControls[a.Id].GetVisualDescendants().OfType<Border>().Single(p=>p.Name=="VisionConnectionPort");
        var portAt=port.TranslatePoint(new Point(6,6),gw)!.Value;var targetAt=v.ObjectControls[b.Id].TranslatePoint(new Point(40,35),gw)!.Value;
        Check(v.DisconnectObjects(a.Id,b.Id),"Could not remove existing connection");Jobs();v.SelectObject(a.Id);v.RefreshBoard();Jobs();
        port=v.ObjectControls[a.Id].GetVisualDescendants().OfType<Border>().Single(p=>p.Name=="VisionConnectionPort");portAt=port.TranslatePoint(new Point(6,6),gw)!.Value;
        gw.MouseDown(portAt,MouseButton.Left);gw.MouseMove(targetAt);gw.MouseUp(targetAt,MouseButton.Left);Jobs();
        Check(v.ConnectionSourceId is null&&v.ConnectionControls.Count==2,"Dragging the connection dot failed");
        var line=v.ConnectionControls.Single(l=>l.TargetId==b.Id);var middle=line.TranslatePoint(line.Curve.At(.5),gw)!.Value;
        gw.MouseDown(middle,MouseButton.Left);gw.MouseUp(middle,MouseButton.Left);Jobs();Check(v.GetVisualDescendants().OfType<Button>().Any(x=>x.Content as string=="remove connection"),"Connector curve was not clickable");
        var empty=line.TranslatePoint(new Point(900,900),gw)!.Value;Check(!ReferenceEquals(gw.InputHitTest(empty),line),"Connector swallowed empty canvas clicks");
        v.SetZoom(.5);Jobs();line=v.ConnectionControls.Single(l=>l.TargetId==b.Id);middle=line.TranslatePoint(line.Curve.At(.5),gw)!.Value;
        gw.MouseDown(middle,MouseButton.Left);gw.MouseUp(middle,MouseButton.Left);Jobs();Check(v.GetVisualDescendants().OfType<Button>().Any(x=>x.Content as string=="remove connection"),"Connection hit test did not track zoom");
        gw.MouseDown(middle,MouseButton.Right);gw.MouseUp(middle,MouseButton.Right);Jobs();Check(v.ConnectionMenu?.IsOpen==true,"Right-click connection menu closed immediately");v.ConnectionMenu!.IsOpen=false;
        graph.Store.SetVisionDeleted(m,b.Id,true);Jobs();Check(v.ConnectionControls.Count==1,"Deleting endpoint left visible connection");graph.Store.SetVisionDeleted(m,b.Id,false);Jobs();Check(v.ConnectionControls.Count==2,"Restoring endpoint did not restore connection");
        v.SelectObject(a.Id);v.SelectObject(b.Id,true);var copy=v.CopySelection();Check(copy is not null&&v.PasteSelection(copy),"Connected selection did not paste");Jobs();Check(v.ConnectionControls.Count==3,"Pasted pair did not carry its internal connection");
        Check(v.BeginConnection(a.Id)&&v.Escape()&&v.ConnectionSourceId is null,"Escape did not cancel connection mode");
        Check(v.ConnectObjects(a.Id,b.Id),"Duplicate connection reported failure");Jobs();Render(gw,"vision-connected-thoughts");
        Check(graph.TryQuit(),"Connected workspace did not quit normally");Jobs();
    }
    Console.WriteLine($"PASS: {checks} headless window/control checks. Synthetic data only; native OS behavior remains unverified.");
}
finally
{
    session?.TryQuit(); session?.Dispose();
    if (Directory.Exists(root)) Directory.Delete(root, true);
}
