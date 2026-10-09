using Avalonia;
using Miche.Mac.Controls;

var checks = 0;
void Check(bool passed, string description)
{
    checks++;
    if (!passed) throw new Exception(description);
}
bool Near(double a, double b) => Math.Abs(a - b) < 1e-6;
bool Same(Point a, Point b) => Near(a.X, b.X) && Near(a.Y, b.Y);
bool Finite(ConnectionCurve curve) => new[] { curve.Start, curve.Control1, curve.Control2, curve.End, curve.ArrowLeft, curve.ArrowRight }
    .All(point => double.IsFinite(point.X) && double.IsFinite(point.Y));
bool OnEllipse(Point point, Rect bounds)
{
    var x = (point.X - bounds.Center.X) / (bounds.Width / 2);
    var y = (point.Y - bounds.Center.Y) / (bounds.Height / 2);
    return Near(x * x + y * y, 1);
}

var source = new Rect(20, 30, 160, 100);
var target = new Rect(420, 30, 160, 100);
var horizontal = ConnectionGeometry.Create(source, target);
Check(Same(horizontal.Start, new Point(source.Right, source.Center.Y)), "Horizontal source attaches at its facing right edge.");
Check(Same(horizontal.End, new Point(target.Left, target.Center.Y)), "Horizontal target attaches at its facing left edge.");
Check(horizontal.Control1.X > horizontal.Start.X && horizontal.Control2.X < horizontal.End.X, "Horizontal handles face into the gap.");
Check(horizontal.ArrowLeft.X < horizontal.End.X && horizontal.ArrowRight.X < horizontal.End.X, "Downstream arrow wings stay outside the target.");
Check(Same(horizontal.At(0), horizontal.Start) && Same(horizontal.At(1), horizontal.End), "Cubic evaluation preserves endpoints.");
Check(horizontal.DistanceTo(horizontal.At(.5)) < .001, "Center of the line can be hit-tested.");
Check(horizontal.DistanceTo(new Point(horizontal.At(.5).X, horizontal.At(.5).Y + 17)) >= 16.999, "A point away from the line reports that distance.");
Check(horizontal.DistanceTo(horizontal.ArrowLeft) < .001, "Arrow wings are included in hit testing.");
Check(horizontal == ConnectionGeometry.Create(source, target), "Routing is deterministic.");

var reverse = ConnectionGeometry.Create(target, source);
Check(Same(reverse.Start, horizontal.End) && Same(reverse.End, horizontal.Start), "Reversing direction swaps facing ports.");
Check(reverse.ArrowLeft.X > reverse.End.X && reverse.ArrowRight.X > reverse.End.X, "A reversed arrow points downstream to the left.");

var below = new Rect(70, 380, 160, 100);
var vertical = ConnectionGeometry.Create(source, below);
Check(Near(vertical.Start.Y, source.Bottom) && Near(vertical.End.Y, below.Top), "A thought below uses bottom/top ports.");
Check(vertical.Control1.Y > vertical.Start.Y && vertical.Control2.Y < vertical.End.Y, "Vertical handles face into the gap.");
Check(vertical.ArrowLeft.Y < vertical.End.Y && vertical.ArrowRight.Y < vertical.End.Y, "Vertical arrow wings stay above the downstream top edge.");
var above = ConnectionGeometry.Create(below, source);
Check(Near(above.Start.Y, below.Top) && Near(above.End.Y, source.Bottom), "A thought above uses top/bottom ports.");

var ovalSource = new Rect(500, 100, 900, 220);
var ovalTarget = new Rect(130, 560, 300, 120);
var ovals = ConnectionGeometry.Create(ovalSource, ovalTarget, true, true);
Check(OnEllipse(ovals.Start, ovalSource) && OnEllipse(ovals.End, ovalTarget), "Ellipse ports lie on actual curved boundaries.");
Check(ovals.Start.Y > ovalSource.Center.Y && ovals.End.Y < ovalTarget.Center.Y, "Offset ellipses use facing lower/upper arcs.");
Check(Finite(ovals), "Offset oval routing produces finite points.");

var ellipseTarget = new Rect(460, 100, 130, 180);
var mixed = ConnectionGeometry.Create(source, ellipseTarget, false, true);
Check(Near(mixed.Start.X, source.Right), "Mixed routing keeps the rectangular source on an edge.");
Check(OnEllipse(mixed.End, ellipseTarget), "Mixed routing keeps the oval destination on its boundary.");

var near = ConnectionGeometry.Create(source, new Rect(source.Right + 3, source.Top, source.Width, source.Height));
Check(near.Control1.X <= near.Control2.X, "A tiny gap does not fold horizontal handles backwards.");
Check(Finite(near), "A tiny gap remains finite.");
var touching = ConnectionGeometry.Create(source, new Rect(source.Right, source.Top, source.Width, source.Height));
Check(Finite(touching) && !Same(touching.Start, touching.End), "Touching identical-height thoughts produce an outer curve instead of a zero-length line.");
var overlap = ConnectionGeometry.Create(source, new Rect(80, 60, 160, 100));
Check(Finite(overlap) && !Same(overlap.Start, overlap.End), "Overlapping thoughts keep a visible, finite curve.");
Check(overlap.Control1.Y > Math.Max(source.Bottom, 160) && overlap.Control2.Y > Math.Max(source.Bottom, 160), "Overlap is routed below the pair when horizontal displacement dominates.");
var same = ConnectionGeometry.Create(source, source, true, true);
Check(Finite(same) && !Same(same.Start, same.End), "Coincident thoughts use distinct boundary ports.");
Check(same.Control1.X > source.Right && same.Control2.Y < source.Top, "Coincident thoughts form an outer loop.");
Check(Finite(ConnectionGeometry.Create(default, default)), "Zero-sized transient bounds have a stable finite fallback.");

var translated = ConnectionGeometry.Create(new Rect(source.X + 31, source.Y - 27, source.Width, source.Height),
    new Rect(target.X + 31, target.Y - 27, target.Width, target.Height));
var before = new[] { horizontal.Start, horizontal.Control1, horizontal.Control2, horizontal.End, horizontal.ArrowLeft, horizontal.ArrowRight };
var after = new[] { translated.Start, translated.Control1, translated.Control2, translated.End, translated.ArrowLeft, translated.ArrowRight };
Check(before.Zip(after).All(pair => Same(new Point(pair.First.X + 31, pair.First.Y - 27), pair.Second)), "Moving both thoughts translates every curve and arrow point consistently.");
var resized = ConnectionGeometry.Create(new Rect(source.X, source.Y, source.Width + 45, source.Height), target);
Check(Near(resized.Start.X, source.Right + 45), "Resizing a thought recomputes its boundary attachment.");

var gridCases = new[]
{
    new Rect(0, 0, 1, 1), new Rect(2, 2, 2, 2), new Rect(5, 100, 500, 20),
    new Rect(100, 5, 20, 500), new Rect(5, 5, 500, 500), new Rect(9000, 9000, 1000, 1000)
};
foreach (var first in gridCases)
foreach (var second in gridCases)
{
    var curve = ConnectionGeometry.Create(first, second, true, true);
    Check(Finite(curve) && OnEllipse(curve.Start, first) && OnEllipse(curve.End, second), "Every size/location pairing anchors finite geometry to both ellipse boundaries.");
    Check(curve.DistanceTo(curve.Start) < .001 && curve.DistanceTo(curve.End) < .001, "Every size/location pairing retains a useful click target at both endpoints.");
}
try { ConnectionGeometry.Create(new Rect(double.NaN, 0, 1, 1), target); Check(false, "Invalid coordinates must reject routing."); }
catch (ArgumentException) { Check(true, "Invalid coordinates reject routing."); }
try { ConnectionGeometry.Create(source, target, arrowSize: double.PositiveInfinity); Check(false, "Invalid arrow sizes must reject routing."); }
catch (ArgumentOutOfRangeException) { Check(true, "Invalid arrow sizes reject routing."); }

Console.WriteLine($"{checks} connection geometry checks passed.");
