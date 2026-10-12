using System;

namespace RotateRings
{
    /// <summary>两点加半径表示胶囊，四点表示实心矩形；完全不调用 Physics2D。</summary>
    public sealed class Ring2DCollisionShape
    {
        public readonly Ring2DPoint[] Points;
        public double Radius;
        public bool IsRingBody;
        public bool RotationInvariant;
        public Ring2DPoint CircleCenter;
        public double HeadPassageRadians;
        public double MinX, MinY, MaxX, MaxY;
        public Ring2DCollisionShape(int count) { Points = new Ring2DPoint[count]; }
        public void UpdateBounds()
        {
            MinX = MaxX = Points[0].X; MinY = MaxY = Points[0].Y;
            foreach (var point in Points)
            {
                MinX = Math.Min(MinX, point.X); MaxX = Math.Max(MaxX, point.X);
                MinY = Math.Min(MinY, point.Y); MaxY = Math.Max(MaxY, point.Y);
            }
            MinX -= Radius; MinY -= Radius; MaxX += Radius; MaxY += Radius;
        }
    }

    public static class Ring2DCollision
    {
        public const double Skin = 0.0001;
        public const double ContactDistance = Skin * 2;

        public static double BoundsDistance(Ring2DCollisionShape a, Ring2DCollisionShape b)
        {
            double x = Math.Max(0, Math.Max(a.MinX - b.MaxX, b.MinX - a.MaxX));
            double y = Math.Max(0, Math.Max(a.MinY - b.MaxY, b.MinY - a.MaxY));
            return Math.Sqrt(x * x + y * y);
        }

        public static double Clearance(Ring2DCollisionShape a, Ring2DCollisionShape b)
        {
            double distance = double.PositiveInfinity;
            int edgesA = a.Points.Length == 2 ? 1 : 4, edgesB = b.Points.Length == 2 ? 1 : 4;
            for (int i = 0; i < edgesA; i++)
                for (int j = 0; j < edgesB; j++)
                    distance = Math.Min(distance, SegmentDistanceSquared(a.Points[i], a.Points[(i + 1) % a.Points.Length],
                        b.Points[j], b.Points[(j + 1) % b.Points.Length]));
            if (Contains(a, b.Points[0]) || Contains(b, a.Points[0])) distance = 0;
            double clearance = Math.Sqrt(distance) - a.Radius - b.Radius;
            if (a.IsRingBody && b.HeadPassageRadians > 0) clearance = Math.Max(clearance, PassageClearance(a, b));
            if (b.IsRingBody && a.HeadPassageRadians > 0) clearance = Math.Max(clearance, PassageClearance(b, a));
            return clearance;
        }

        /// <summary>最近轮廓表面的中点（世界 XY），供接触日志和动画使用。</summary>
        public static Ring2DPoint ContactPoint(Ring2DCollisionShape a, Ring2DCollisionShape b)
        {
            double best = double.PositiveInfinity;
            Ring2DPoint pointA = a.Points[0], pointB = b.Points[0];
            int edgesA = a.Points.Length == 2 ? 1 : 4, edgesB = b.Points.Length == 2 ? 1 : 4;
            for (int i = 0; i < edgesA; i++) for (int j = 0; j < edgesB; j++)
            {
                Ring2DPoint p, q;
                ClosestSegmentPoints(a.Points[i], a.Points[(i+1)%a.Points.Length],
                    b.Points[j], b.Points[(j+1)%b.Points.Length], out p, out q);
                double x = q.X-p.X, y = q.Y-p.Y, distance = x*x+y*y;
                if (distance < best) { best = distance; pointA = p; pointB = q; }
            }
            double length = Math.Sqrt(best);
            double offset = length > 1e-12 ? (a.Radius-b.Radius)/length : 0;
            return new Ring2DPoint((pointA.X+pointB.X+(pointB.X-pointA.X)*offset)/2,
                (pointA.Y+pointB.Y+(pointB.Y-pointA.Y)*offset)/2);
        }

        /// <summary>卡扣通道沿头的横轴；用角度余量限制推进，避免忽略碰撞后跨过允许角度。</summary>
        private static double PassageClearance(Ring2DCollisionShape body, Ring2DCollisionShape head)
        {
            double bx = body.Points[1].X-body.Points[0].X, by = body.Points[1].Y-body.Points[0].Y;
            double hx = head.Points[1].X-head.Points[0].X, hy = head.Points[1].Y-head.Points[0].Y;
            double bodyLength = Math.Sqrt(bx*bx+by*by), headLength = Math.Sqrt(hx*hx+hy*hy);
            if (bodyLength <= 1e-12 || headLength <= 1e-12) return double.NegativeInfinity;
            // 同一个卡扣只按其中心对应的圆环切线判断，避免两侧曲率和离散分段改变通行角度。
            double rx = (head.Points[0].X+head.Points[2].X)/2-body.CircleCenter.X;
            double ry = (head.Points[0].Y+head.Points[2].Y)/2-body.CircleCenter.Y;
            double radialLength = Math.Sqrt(rx*rx+ry*ry);
            if (radialLength <= 1e-12) return double.NegativeInfinity;
            double cosine = Math.Min(1, Math.Abs(-ry*hx+rx*hy)/(radialLength*headLength));
            double margin = head.HeadPassageRadians-Math.Acos(cosine);
            return Math.Sin(margin)*Math.Min(radialLength, Math.Min(bodyLength, headLength)/2);
        }

        private static bool Contains(Ring2DCollisionShape shape, Ring2DPoint point)
        {
            if (shape.Points.Length != 4) return false;
            bool positive = false, negative = false;
            for (int i = 0; i < 4; i++)
            {
                double cross = Cross(shape.Points[i], shape.Points[(i + 1) % 4], point);
                positive |= cross > 0; negative |= cross < 0;
            }
            return !(positive && negative);
        }

        private static double Cross(Ring2DPoint a, Ring2DPoint b, Ring2DPoint p)
        { return (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X); }

        private static void ClosestSegmentPoints(Ring2DPoint a, Ring2DPoint b, Ring2DPoint c, Ring2DPoint d,
            out Ring2DPoint p, out Ring2DPoint q)
        {
            double ux = b.X-a.X, uy = b.Y-a.Y, vx = d.X-c.X, vy = d.Y-c.Y, rx = a.X-c.X, ry = a.Y-c.Y;
            double uu = ux*ux+uy*uy, vv = vx*vx+vy*vy, uv = ux*vx+uy*vy, ur = ux*rx+uy*ry, vr = vx*rx+vy*ry;
            double s = 0, t = 0;
            if (uu <= 1e-24) t = vv > 1e-24 ? Clamp01(vr/vv) : 0;
            else if (vv <= 1e-24) s = Clamp01(-ur/uu);
            else
            {
                double denominator = uu*vv-uv*uv;
                s = denominator > 1e-24 ? Clamp01((uv*vr-ur*vv)/denominator) : 0;
                t = (uv*s+vr)/vv;
                if (t < 0) { t = 0; s = Clamp01(-ur/uu); }
                else if (t > 1) { t = 1; s = Clamp01((uv-ur)/uu); }
            }
            p = new Ring2DPoint(a.X+s*ux, a.Y+s*uy); q = new Ring2DPoint(c.X+t*vx, c.Y+t*vy);
        }

        private static double Clamp01(double value) { return Math.Max(0, Math.Min(1, value)); }

        private static double SegmentDistanceSquared(Ring2DPoint a, Ring2DPoint b, Ring2DPoint c, Ring2DPoint d)
        {
            Ring2DPoint p, q; ClosestSegmentPoints(a, b, c, d, out p, out q);
            double x = p.X-q.X, y = p.Y-q.Y; return x*x+y*y;
        }

        /// <summary>以轮廓最大位移和当前间距限制每一步，避免只检查终点导致穿透。</summary>
        public static double Sweep(double travel, Func<double, double> clearance)
        {
            bool blocked;
            return Sweep(travel, clearance, out blocked);
        }

        public static double Sweep(double travel, Func<double, double> clearance, out bool blocked, bool holdAtContact = false)
        {
            blocked = false;
            if (travel <= 1e-12) return 0;
            double fraction = 0, distance = clearance(0);
            if (holdAtContact && distance <= ContactDistance) { blocked = true; return 0; }
            // ponytail: 最多 512 次保守推进；复杂布局达到上限时停在安全位置，绝不跳到终点。
            for (int i = 0; i < 512 && fraction < 1; i++)
            {
                // 接触附近只试探半个 skin，允许反向离开，同时维持正间距。
                double step = Math.Max(Skin / 2, (distance - Skin) * 0.8) / travel;
                double next = Math.Min(1, fraction + step), nextDistance = clearance(next);
                if (nextDistance < Skin && nextDistance <= distance + 1e-12) { blocked = true; return fraction; }
                fraction = next; distance = nextDistance;
            }
            return fraction;
        }
    }
}
