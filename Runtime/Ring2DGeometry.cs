using System;
using System.Collections.Generic;

namespace RotateRings
{
    public enum Ring2DShape { Circle, Straight, CShape, Gate, Seven, ZShape }
    public enum Ring2DConnectionDirection { Outward, Inward }

    public struct RingVertex { public float X, Y, NZ, U, V; }
    public sealed class RingMeshData { public RingVertex[] Vertices; public int[] Triangles; }

    /// <summary>二维路径参数，使用世界局部单位；门字、7/Z 均用 CornerRadius 将转角圆滑连接。</summary>
    [Serializable]
    public sealed class Ring2DSettings
    {
        public Ring2DShape Shape = Ring2DShape.Circle;
        public float Radius = 1, Width = 2.2f, Height = 2.2f, CornerRadius = 0.4f, Thickness = 0.22f;
        // 0 仅用于兼容旧场景：旧 Z 形的两段宽度均为 Width/2，Inspector 修改后写入独立值。
        public float TopWidth, BottomWidth;
        public float ZTopWidth { get { return TopWidth == 0 ? Width / 2 : TopWidth; } }
        public float ZBottomWidth { get { return BottomWidth == 0 ? Width / 2 : BottomWidth; } }
        public float StartDegrees = 45, SweepDegrees = 270;
        public int Segments = 96, CapSegments = 16;
        public void Validate()
        {
            foreach (float value in new[] { Radius, Width, Height, CornerRadius, Thickness, StartDegrees, SweepDegrees, TopWidth, BottomWidth })
                if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentException("形状参数必须为有限数值。");
            if (!Enum.IsDefined(typeof(Ring2DShape), Shape) || Radius <= 0 || Width <= 0 || Height <= 0 || Thickness <= 0)
                throw new ArgumentException("形状或尺寸无效。");
            if ((Shape == Ring2DShape.Circle || Shape == Ring2DShape.CShape) && Thickness >= Radius * 2)
                throw new ArgumentException("环身太粗，内孔消失。");
            if (Shape == Ring2DShape.CShape && (SweepDegrees <= 0 || SweepDegrees >= 360)) throw new ArgumentException("C 环覆盖角须介于 0 和 360°。");
            if (Shape == Ring2DShape.Gate && (CornerRadius <= Thickness / 2 || CornerRadius > Math.Min(Width / 2, Height)))
                throw new ArgumentException("门字环圆角须大于环身半宽，且不超过半宽或高度。");
            if (Shape == Ring2DShape.Seven && (CornerRadius <= Thickness / 2 || CornerRadius > Math.Min(Width, Height)))
                throw new ArgumentException("7 字环圆角须大于环身半宽，且不超过宽度或高度。");
            if (Shape == Ring2DShape.ZShape && (TopWidth < 0 || BottomWidth < 0 || CornerRadius <= Thickness / 2 || CornerRadius > Math.Min(Math.Min(ZTopWidth, ZBottomWidth), Height / 2)))
                throw new ArgumentException("Z 字环圆角须大于环身半宽，且不超过上横段宽度、下横段宽度或半高。");
            if (Segments < 8 || Segments > 512 || CapSegments < 4 || CapSegments > 64) throw new ArgumentException("分段范围无效。");
        }
    }

    /// <summary>一个连接点；位置和尺寸由父级维护，实际显示为连接杆和垂直连接头两个子对象。</summary>
    [Serializable]
    public sealed class Ring2DConnection
    {
        public string Name = "连接点";
        public float AngleDegrees = 90;
        public float PathPosition = 0.5f;
        public Ring2DConnectionDirection Direction = Ring2DConnectionDirection.Inward;
        public bool ToCenter = true;
        public float Length = 0.5f, RodWidth = 0.14f, DirectionOffsetDegrees;
        public bool ShowHead = true;
        public float HeadWidth = 0.42f, HeadDepth = 0.22f;
        // 头作为带通道的卡扣，圆/C 环身沿头的横轴滑过；杆仍为实体。
        public bool AllowRingBodyPassage = true;
        public float PassageAngleDegrees = 30;
        public void Validate()
        {
            foreach (float v in new[] { AngleDegrees, PathPosition, Length, RodWidth, DirectionOffsetDegrees, HeadWidth, HeadDepth, PassageAngleDegrees })
                if (float.IsNaN(v) || float.IsInfinity(v)) throw new ArgumentException("连接参数必须为有限数值。");
            if (!Enum.IsDefined(typeof(Ring2DConnectionDirection), Direction) || PathPosition < 0 || PathPosition > 1 ||
                Length < 0 || RodWidth <= 0 || HeadWidth <= 0 || HeadDepth <= 0 || PassageAngleDegrees < 0 || PassageAngleDegrees > 90)
                throw new ArgumentException("连接位置、尺寸或通行角度无效。");
        }
    }

    public struct Ring2DPoint { public double X, Y; public Ring2DPoint(double x, double y) { X = x; Y = y; } }
    public struct Ring2DConnectionPose
    {
        public Ring2DPoint Anchor, Start, End, Head;
        public double DX, DY, Length;
    }

    public static class Ring2DGeometry
    {
        public static double PathLength(Ring2DSettings s)
        {
            if (s.Shape == Ring2DShape.Straight) return s.Width;
            if (s.Shape == Ring2DShape.Seven) return (double)s.Width + s.Height + (Math.PI / 2 - 2) * s.CornerRadius;
            if (s.Shape == Ring2DShape.ZShape) return (double)s.ZTopWidth + s.ZBottomWidth + s.Height + (Math.PI - 4) * s.CornerRadius;
            if (s.Shape == Ring2DShape.Gate) return 2 * ((double)s.Height - s.CornerRadius) + s.Width - 2 * (double)s.CornerRadius + Math.PI * s.CornerRadius;
            return s.Radius * Math.PI / 180 * (s.Shape == Ring2DShape.Circle ? 360 : s.SweepDegrees);
        }

        /// <summary>求路径位置、单位切线和外侧法线；圆形按角度，门字环按路径长度采样。</summary>
        public static void Frame(Ring2DSettings s, double t, out double x, out double y, out double tx, out double ty, out double nx, out double ny)
        {
            if (s.Shape == Ring2DShape.Straight)
            { x = (t - 0.5) * s.Width; y = 0; tx = 1; ty = 0; nx = 0; ny = 1; return; }
            if (s.Shape == Ring2DShape.Seven)
            {
                double r = s.CornerRadius, straight = s.Width - r, arc = Math.PI * r / 2, distance = t * PathLength(s);
                if (distance <= straight) { x = -s.Width / 2 + distance; y = s.Height / 2; tx = 1; ty = 0; }
                else if (distance <= straight + arc)
                {
                    double angle = Math.PI / 2 - (distance - straight) / r;
                    x = s.Width / 2 - r + r * Math.Cos(angle); y = s.Height / 2 - r + r * Math.Sin(angle);
                    tx = Math.Sin(angle); ty = -Math.Cos(angle);
                }
                else { x = s.Width / 2; y = s.Height / 2 - r - (distance - straight - arc); tx = 0; ty = -1; }
                nx = -ty; ny = tx;
                return;
            }
            if (s.Shape == Ring2DShape.ZShape)
            {
                double r = s.CornerRadius, horizontal = s.ZTopWidth - r, arc = Math.PI * r / 2;
                double second = horizontal + arc + s.Height - 2 * r, distance = t * PathLength(s);
                if (distance <= horizontal) { x = -s.ZTopWidth + distance; y = s.Height / 2; tx = 1; ty = 0; }
                else if (distance <= horizontal + arc)
                {
                    double angle = Math.PI / 2 - (distance - horizontal) / r;
                    x = -r + r * Math.Cos(angle); y = s.Height / 2 - r + r * Math.Sin(angle);
                    tx = Math.Sin(angle); ty = -Math.Cos(angle);
                }
                else if (distance <= second) { x = 0; y = s.Height / 2 - r - (distance - horizontal - arc); tx = 0; ty = -1; }
                else if (distance <= second + arc)
                {
                    double angle = Math.PI + (distance - second) / r;
                    x = r + r * Math.Cos(angle); y = -s.Height / 2 + r + r * Math.Sin(angle);
                    tx = -Math.Sin(angle); ty = Math.Cos(angle);
                }
                else { x = r + distance - second - arc; y = -s.Height / 2; tx = 1; ty = 0; }
                nx = -ty; ny = tx;
                return;
            }
            if (s.Shape != Ring2DShape.Gate)
            {
                double a = (s.StartDegrees + t * (s.Shape == Ring2DShape.Circle ? 360 : s.SweepDegrees)) * Math.PI / 180;
                nx = Math.Cos(a); ny = Math.Sin(a); x = s.Radius * nx; y = s.Radius * ny; tx = -ny; ty = nx; return;
            }
            double leg = (double)s.Height - s.CornerRadius, gateArc = Math.PI * s.CornerRadius / 2, top = s.Width - 2 * (double)s.CornerRadius;
            double d = t * PathLength(s), gateAngle;
            if (d <= leg) { x = -s.Width / 2; y = -s.Height / 2 + d; tx = 0; ty = 1; }
            else if ((d -= leg) <= gateArc)
            {
                gateAngle = Math.PI - d / s.CornerRadius;
                x = -s.Width / 2 + s.CornerRadius + s.CornerRadius * Math.Cos(gateAngle);
                y = s.Height / 2 - s.CornerRadius + s.CornerRadius * Math.Sin(gateAngle); tx = Math.Sin(gateAngle); ty = -Math.Cos(gateAngle);
            }
            else if ((d -= gateArc) <= top) { x = -s.Width / 2 + s.CornerRadius + d; y = s.Height / 2; tx = 1; ty = 0; }
            else if ((d -= top) <= gateArc)
            {
                gateAngle = Math.PI / 2 - d / s.CornerRadius;
                x = s.Width / 2 - s.CornerRadius + s.CornerRadius * Math.Cos(gateAngle);
                y = s.Height / 2 - s.CornerRadius + s.CornerRadius * Math.Sin(gateAngle); tx = Math.Sin(gateAngle); ty = -Math.Cos(gateAngle);
            }
            else { d -= gateArc; x = s.Width / 2; y = s.Height / 2 - s.CornerRadius - d; tx = 0; ty = -1; }
            // 门字路径是顺时针，外侧是切线左边；圆形路径则是逆时针。
            nx = -ty; ny = tx;
        }

        /// <summary>A 图被视为独立的胶囊 Sprite，UV 为 Sprite 内部 0..1；不依赖它在原图中的位置。</summary>
        public static RingMeshData Build(Ring2DSettings s, double capFraction)
        {
            s.Validate();
            if (double.IsNaN(capFraction) || double.IsInfinity(capFraction) || capFraction <= 0 || capFraction >= 0.5)
                throw new ArgumentException("A 图圆头比例须介于 0 和 0.5。");
            var vertices = new List<RingVertex>(); var triangles = new List<int>();
            var knots = new SortedSet<double> { 0, 1 };
            if (s.Shape == Ring2DShape.Gate || s.Shape == Ring2DShape.Seven || s.Shape == Ring2DShape.ZShape)
                knots = RoundedKnots(s, s.Segments);
            else if (s.Shape != Ring2DShape.Straight)
                for (int i = 1; i < s.Segments; i++) knots.Add((double)i / s.Segments);
            double previous = -1;
            foreach (double t in knots)
            {
                if (t <= previous + 1e-12) continue;
                if (previous >= 0)
                {
                    int a = vertices.Count;
                    Pair(s, previous, capFraction, vertices); Pair(s, t, capFraction, vertices);
                    Tri(vertices, triangles, a, a + 1, a + 2); Tri(vertices, triangles, a + 1, a + 3, a + 2);
                }
                previous = t;
            }
            if (s.Shape != Ring2DShape.Circle) AppendCaps(s, capFraction, vertices, triangles);
            return new RingMeshData { Vertices = vertices.ToArray(), Triangles = triangles.ToArray() };
        }

        /// <summary>门字/7/Z 共用圆角采样，只细分四分之一圆弧，保留所有直边与圆弧连接点。</summary>
        private static SortedSet<double> RoundedKnots(Ring2DSettings s, int segments)
        {
            double r = s.CornerRadius, arc = Math.PI * r / 2, length = PathLength(s);
            double[] starts = s.Shape == Ring2DShape.Seven ? new[] { s.Width - r }
                : s.Shape == Ring2DShape.ZShape ? new[] { s.ZTopWidth - r, s.ZTopWidth - r + arc + s.Height - 2 * r }
                : new[] { s.Height - r, s.Height - r + arc + s.Width - 2 * r };
            var knots = new SortedSet<double> { 0, 1 };
            int count = (int)Math.Ceiling(segments / 4.0);
            foreach (double start in starts)
                for (int i = 0; i <= count; i++) knots.Add(Math.Max(0, Math.Min(1, (start + arc * i / count) / length)));
            return knots;
        }

        /// <summary>在开放路径两端补 A 图圆头；拐角本身不会添加圆角。</summary>
        private static void AppendCaps(Ring2DSettings s, double capFraction, List<RingVertex> vertices, List<int> triangles)
        {
            for (int end = 0; end < 2; end++)
            {
                double x, y, tx, ty, nx, ny; Frame(s, end, out x, out y, out tx, out ty, out nx, out ny);
                double sign = end == 0 ? -1 : 1, u = end == 0 ? capFraction : 1 - capFraction;
                int center = vertices.Count; Add(vertices, x, y, u, 0.5);
                for (int i = 0; i <= s.CapSegments; i++)
                {
                    double a = -Math.PI / 2 + Math.PI * i / s.CapSegments, across = Math.Sin(a), outward = Math.Cos(a);
                    Add(vertices, x + (sign * tx * outward + nx * across) * s.Thickness / 2,
                        y + (sign * ty * outward + ny * across) * s.Thickness / 2, u + sign * capFraction * outward, 0.5 + across / 2);
                    if (i > 0) Tri(vertices, triangles, center, center + i, center + i + 1);
                }
            }
        }

        private static void Pair(Ring2DSettings s, double t, double cap, List<RingVertex> vertices)
        {
            double x, y, tx, ty, nx, ny; Frame(s, t, out x, out y, out tx, out ty, out nx, out ny);
            double u = cap + t * (1 - cap * 2);
            Add(vertices, x + nx * s.Thickness / 2, y + ny * s.Thickness / 2, u, 1);
            Add(vertices, x - nx * s.Thickness / 2, y - ny * s.Thickness / 2, u, 0);
        }
        private static void Add(List<RingVertex> v, double x, double y, double u, double uvY)
        { v.Add(new RingVertex { X = (float)x, Y = (float)y, NZ = -1, U = (float)u, V = (float)uvY }); }
        private static void Tri(List<RingVertex> v, List<int> indices, int a, int b, int c)
        {
            bool clockwise = (v[b].X - v[a].X) * (v[c].Y - v[a].Y) - (v[b].Y - v[a].Y) * (v[c].X - v[a].X) < 0;
            indices.Add(a); indices.Add(clockwise ? b : c); indices.Add(clockwise ? c : b);
        }

        /// <summary>连接杆与头的位置：自动向内模式把头中心放在原点；手动长度从环边缘附近起算。</summary>
        public static Ring2DConnectionPose ConnectionPose(Ring2DSettings s, Ring2DConnection connection)
        {
            s.Validate(); connection.Validate();
            double x, y, tx, ty, nx, ny;
            if (s.Shape == Ring2DShape.Circle || s.Shape == Ring2DShape.CShape)
            {
                double delta = ((connection.AngleDegrees - s.StartDegrees) % 360 + 360) % 360;
                if (s.Shape == Ring2DShape.CShape && delta > s.SweepDegrees + 0.00001) throw new ArgumentException("连接点位于 C 环缺口中。");
                double angle = connection.AngleDegrees * Math.PI / 180;
                nx = Math.Cos(angle); ny = Math.Sin(angle); x = s.Radius * nx; y = s.Radius * ny;
            }
            else
            {
                Frame(s, connection.PathPosition, out x, out y, out tx, out ty, out nx, out ny);
                if (s.Shape == Ring2DShape.Straight) { nx = connection.PathPosition < 0.5 ? -1 : 1; ny = 0; }
            }
            bool center = connection.Direction == Ring2DConnectionDirection.Inward && connection.ToCenter;
            double dx = nx, dy = ny;
            if (center)
            {
                double distance = Math.Sqrt(x * x + y * y);
                if (distance > 1e-9) { dx = -x / distance; dy = -y / distance; }
                else { dx = -nx; dy = -ny; }
            }
            else
            {
                if (connection.Direction == Ring2DConnectionDirection.Inward) { dx = -dx; dy = -dy; }
                double offset = connection.DirectionOffsetDegrees * Math.PI / 180;
                double turnedX = dx * Math.Cos(offset) - dy * Math.Sin(offset);
                dy = dx * Math.Sin(offset) + dy * Math.Cos(offset); dx = turnedX;
            }
            double overlap = Math.Min(0.01, Math.Min(s.Thickness, connection.RodWidth) / 4);
            double startOffset = center && Math.Sqrt(x * x + y * y) <= s.Thickness / 2 ? 0 : s.Thickness / 2 - overlap;
            var start = new Ring2DPoint(x + dx * startOffset, y + dy * startOffset);
            double headHalf = connection.ShowHead ? connection.HeadDepth / 2 : 0;
            double rodLength = center ? Math.Max(0, Math.Sqrt(start.X * start.X + start.Y * start.Y) - headHalf + (connection.ShowHead ? overlap : 0)) : connection.Length;
            var end = new Ring2DPoint(start.X + dx * rodLength, start.Y + dy * rodLength);
            var head = center ? new Ring2DPoint(0, 0) : new Ring2DPoint(end.X + dx * Math.Max(0, headHalf - overlap), end.Y + dy * Math.Max(0, headHalf - overlap));
            return new Ring2DConnectionPose { Anchor = new Ring2DPoint(x, y), Start = start, End = end, Head = head, DX = dx, DY = dy, Length = rodLength };
        }

        /// <summary>低分段碰撞路径：圆/C 按角度采样，门字/7/Z 仅细分圆角，直边只保留端点。</summary>
        public static Ring2DPoint[] ColliderPath(Ring2DSettings s, int segments)
        {
            s.Validate();
            if (segments < 8 || segments > 128) throw new ArgumentException("碰撞分段须介于 8 和 128。");
            var knots = new SortedSet<double> { 0, 1 };
            if (s.Shape == Ring2DShape.Seven || s.Shape == Ring2DShape.ZShape || s.Shape == Ring2DShape.Gate) knots = RoundedKnots(s, segments);
            else if (s.Shape != Ring2DShape.Straight)
            {
                int count = s.Shape == Ring2DShape.Circle ? segments : Math.Max(1, (int)Math.Ceiling(segments * s.SweepDegrees / 360.0));
                for (int i = 1; i < count; i++) knots.Add((double)i / count);
            }
            var points = new List<Ring2DPoint>();
            double previous = -1;
            foreach (double t in knots)
            {
                if (t <= previous + 1e-9) continue;
                double x, y, tx, ty, nx, ny;
                Frame(s, s.Shape == Ring2DShape.Circle && t == 1 ? 0 : t, out x, out y, out tx, out ty, out nx, out ny);
                points.Add(new Ring2DPoint(x, y)); previous = t;
            }
            return points.ToArray();
        }

        public static Ring2DPoint[] Centerline(Ring2DSettings s)
        {
            if (s.Shape == Ring2DShape.Seven || s.Shape == Ring2DShape.ZShape) return ColliderPath(s, Math.Min(s.Segments, 128));
            int count = s.Shape == Ring2DShape.Straight ? 1 : s.Segments;
            var points = new Ring2DPoint[count + 1];
            for (int i = 0; i <= count; i++)
            {
                double x, y, tx, ty, nx, ny; Frame(s, s.Shape == Ring2DShape.Circle && i == count ? 0 : (double)i / count, out x, out y, out tx, out ty, out nx, out ny);
                points[i] = new Ring2DPoint(x, y);
            }
            return points;
        }

    }
}

