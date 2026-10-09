using Avalonia;

namespace Miche.Mac.Controls;

/// <summary>A directed cubic curve and its two arrow wings, all in board coordinates.</summary>
public readonly record struct ConnectionCurve(
    Point Start, Point Control1, Point Control2, Point End, Point ArrowLeft, Point ArrowRight)
{
    public Point At(double amount)
    {
        var t = Math.Clamp(amount, 0, 1);
        var u = 1 - t;
        return new Point(
            u * u * u * Start.X + 3 * u * u * t * Control1.X + 3 * u * t * t * Control2.X + t * t * t * End.X,
            u * u * u * Start.Y + 3 * u * u * t * Control1.Y + 3 * u * t * t * Control2.Y + t * t * t * End.Y);
    }

    /// <summary>Distance to the visible connector, for a small click target around a fine line.</summary>
    public double DistanceTo(Point point)
    {
        // A fixed subdivision is stable across zoom levels and does not depend on a renderer.
        var distance = double.PositiveInfinity;
        var previous = Start;
        for (var segment = 1; segment <= 48; segment++)
        {
            var next = At(segment / 48d);
            distance = Math.Min(distance, SegmentDistance(point, previous, next));
            previous = next;
        }
        return Math.Min(distance, Math.Min(SegmentDistance(point, End, ArrowLeft), SegmentDistance(point, End, ArrowRight)));
    }

    private static double SegmentDistance(Point point, Point start, Point end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var lengthSquared = dx * dx + dy * dy;
        var t = lengthSquared <= 1e-12 ? 0 : Math.Clamp(((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared, 0, 1);
        var x = point.X - (start.X + t * dx);
        var y = point.Y - (start.Y + t * dy);
        return Math.Sqrt(x * x + y * y);
    }
}

/// <summary>Pure boundary routing; recompute whenever either thought moves or changes size.</summary>
public static class ConnectionGeometry
{
    private enum Side { Left, Right, Top, Bottom }
    private readonly record struct Port(Point Point, Point Normal);

    public static ConnectionCurve Create(Rect source, Rect target, bool sourceEllipse = false, bool targetEllipse = false, double arrowSize = 9)
    {
        source = UsableBounds(source);
        target = UsableBounds(target);
        if (!double.IsFinite(arrowSize) || arrowSize < 0)
            throw new ArgumentOutOfRangeException(nameof(arrowSize));

        var sourceCenter = Center(source);
        var targetCenter = Center(target);
        var dx = targetCenter.X - sourceCenter.X;
        var dy = targetCenter.Y - sourceCenter.Y;
        var horizontalGap = Math.Max(target.Left - source.Right, source.Left - target.Right);
        var verticalGap = Math.Max(target.Top - source.Bottom, source.Top - target.Bottom);

        // Pick the separated axis before considering direction. A tall oval directly below
        // a wide one should attach at bottom/top, even when their centers are offset.
        var horizontal = horizontalGap >= 0 && (verticalGap < 0 || Math.Abs(dx) >= Math.Abs(dy));
        var separated = horizontalGap >= 0 || verticalGap >= 0;
        if (separated)
        {
            var sourceSide = horizontal ? (dx >= 0 ? Side.Right : Side.Left) : (dy >= 0 ? Side.Bottom : Side.Top);
            var targetSide = Opposite(sourceSide);
            var start = Anchor(source, sourceSide, targetCenter, sourceEllipse, true);
            var end = Anchor(target, targetSide, sourceCenter, targetEllipse, true);
            var gap = horizontal ? horizontalGap : verticalGap;
            if (Length(end.Point - start.Point) > 1e-6)
            {
                var handle = Math.Clamp(Length(end.Point - start.Point) * .45, 12, 240);
                // Nearby thoughts stay smooth without a backward loop between crossing handles.
                var axisNormal = horizontal
                    ? Math.Max(Math.Abs(start.Normal.X), Math.Abs(end.Normal.X))
                    : Math.Max(Math.Abs(start.Normal.Y), Math.Abs(end.Normal.Y));
                if (gap < 80) handle = Math.Min(handle, Math.Max(0, gap) * .48 / Math.Max(.05, axisNormal));
                return Make(start, end, handle, arrowSize);
            }
        }

        // Overlapping rectangles have no facing gap. Route around their exterior instead of
        // producing a collapsed line. Equal centers use two different sides, creating a loop.
        if (Math.Abs(dx) < 1e-6 && Math.Abs(dy) < 1e-6)
        {
            var start = Anchor(source, Side.Right, targetCenter, sourceEllipse, false);
            var end = Anchor(target, Side.Top, sourceCenter, targetEllipse, false);
            var handle = Math.Max(40, Math.Max(Math.Max(source.Width, source.Height), Math.Max(target.Width, target.Height)) * .8);
            return Make(start, end, handle, arrowSize);
        }

        if (Math.Abs(dy) >= Math.Abs(dx))
        {
            var side = dx < 0 ? Side.Left : Side.Right;
            var start = Anchor(source, side, targetCenter, sourceEllipse, false);
            var end = Anchor(target, side, sourceCenter, targetEllipse, false);
            var outsideX = side == Side.Right ? Math.Max(source.Right, target.Right) + 48 : Math.Min(source.Left, target.Left) - 48;
            return WithControls(start.Point, new Point(outsideX, start.Point.Y), new Point(outsideX, end.Point.Y), end.Point, arrowSize, end.Normal);
        }
        else
        {
            var side = dy < 0 ? Side.Top : Side.Bottom;
            var start = Anchor(source, side, targetCenter, sourceEllipse, false);
            var end = Anchor(target, side, sourceCenter, targetEllipse, false);
            var outsideY = side == Side.Bottom ? Math.Max(source.Bottom, target.Bottom) + 48 : Math.Min(source.Top, target.Top) - 48;
            return WithControls(start.Point, new Point(start.Point.X, outsideY), new Point(end.Point.X, outsideY), end.Point, arrowSize, end.Normal);
        }
    }

    private static ConnectionCurve Make(Port start, Port end, double handle, double arrowSize) =>
        WithControls(start.Point, Offset(start.Point, start.Normal, handle), Offset(end.Point, end.Normal, handle), end.Point, arrowSize, end.Normal);

    private static ConnectionCurve WithControls(Point start, Point control1, Point control2, Point end, double arrowSize, Point endNormal)
    {
        var tangent = Normalize(end - control2);
        if (Length(tangent) < 1e-6) tangent = new Point(-endNormal.X, -endNormal.Y);
        var arrowBack = Offset(end, tangent, -arrowSize);
        var perpendicular = new Point(-tangent.Y, tangent.X);
        return new ConnectionCurve(start, control1, control2, end,
            Offset(arrowBack, perpendicular, arrowSize * .42), Offset(arrowBack, perpendicular, -arrowSize * .42));
    }

    private static Port Anchor(Rect rect, Side side, Point otherCenter, bool ellipse, bool bias)
    {
        var center = Center(rect);
        var rx = rect.Width / 2;
        var ry = rect.Height / 2;
        var horizontal = side is Side.Left or Side.Right;
        var sign = side is Side.Left or Side.Top ? -1 : 1;
        var offset = !bias ? 0 : horizontal
            ? Math.Clamp((otherCenter.Y - center.Y) * .28, -rect.Height * .28, rect.Height * .28)
            : Math.Clamp((otherCenter.X - center.X) * .28, -rect.Width * .28, rect.Width * .28);
        var x = horizontal ? sign * rx : offset;
        var y = horizontal ? offset : sign * ry;
        if (ellipse)
        {
            if (horizontal) x = sign * rx * Math.Sqrt(Math.Max(0, 1 - y * y / (ry * ry)));
            else y = sign * ry * Math.Sqrt(Math.Max(0, 1 - x * x / (rx * rx)));
        }
        var normal = ellipse ? Normalize(new Point(x / (rx * rx), y / (ry * ry)))
            : horizontal ? new Point(sign, 0) : new Point(0, sign);
        return new Port(new Point(center.X + x, center.Y + y), normal);
    }

    private static Rect UsableBounds(Rect rect)
    {
        if (!double.IsFinite(rect.X) || !double.IsFinite(rect.Y) || !double.IsFinite(rect.Width) || !double.IsFinite(rect.Height))
            throw new ArgumentException("Connection bounds must be finite.");
        var width = Math.Max(1, rect.Width);
        var height = Math.Max(1, rect.Height);
        return new Rect(rect.X - (width - rect.Width) / 2, rect.Y - (height - rect.Height) / 2, width, height);
    }

    private static Side Opposite(Side side) => side switch
    {
        Side.Left => Side.Right, Side.Right => Side.Left, Side.Top => Side.Bottom, _ => Side.Top
    };
    private static Point Center(Rect rect) => new(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
    private static Point Offset(Point point, Point direction, double amount) => new(point.X + direction.X * amount, point.Y + direction.Y * amount);
    private static double Length(Vector vector) => Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y);
    private static double Length(Point point) => Math.Sqrt(point.X * point.X + point.Y * point.Y);
    private static Point Normalize(Vector vector)
    {
        var length = Length(vector);
        return length <= 1e-12 ? default : new Point(vector.X / length, vector.Y / length);
    }
    private static Point Normalize(Point point)
    {
        var length = Length(point);
        return length <= 1e-12 ? default : new Point(point.X / length, point.Y / length);
    }
}
