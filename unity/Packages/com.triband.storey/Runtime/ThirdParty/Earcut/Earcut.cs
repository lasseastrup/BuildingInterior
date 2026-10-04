// Earcut 3.0.1 (https://github.com/mapbox/earcut), ported from JavaScript to C#.
// ISC License, Copyright (c) 2016, Mapbox. See LICENSE.txt beside this file.
//
// The port keeps the original's structure and names so it can be diffed against upstream.
// Behaviour is the same: a fast ear-clipping triangulator for polygons with holes, tolerant of
// degenerate input, returning triangle vertex indices into the flat coordinate array.
#nullable enable annotations   // the port marks nullable references; annotations only, no warnings

using System;
using System.Collections.Generic;

namespace Triband.Storey.ThirdParty
{
    public static class Earcut
    {
        sealed class Node
        {
            public int i;
            public double x, y;
            public Node prev = null!, next = null!;
            public int z;
            public Node? prevZ, nextZ;
            public bool steiner;
            public Node(int i, double x, double y) { this.i = i; this.x = x; this.y = y; }
        }

        /// <summary>
        /// Triangulate a polygon. <paramref name="data"/> is a flat array of vertex coordinates,
        /// <paramref name="holeIndices"/> the vertex index at which each hole ring starts.
        /// </summary>
        public static List<int> Tessellate(double[] data, int[]? holeIndices, int dim = 2)
        {
            bool hasHoles = holeIndices != null && holeIndices.Length > 0;
            int outerLen = hasHoles ? holeIndices![0] * dim : data.Length;
            var outerNode = LinkedList(data, 0, outerLen, dim, true);
            var triangles = new List<int>();

            if (outerNode == null || outerNode.next == outerNode.prev) return triangles;

            double minX = 0, minY = 0, invSize = 0;

            if (hasHoles) outerNode = EliminateHoles(data, holeIndices!, outerNode, dim);

            // if the shape is not too simple, we'll use z-order curve hash later; calculate polygon bbox
            if (data.Length > 80 * dim)
            {
                minX = double.PositiveInfinity; minY = double.PositiveInfinity;
                double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
                for (int i = dim; i < outerLen; i += dim)
                {
                    double x = data[i], y = data[i + 1];
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
                invSize = Math.Max(maxX - minX, maxY - minY);
                invSize = invSize != 0 ? 32767 / invSize : 0;
            }

            EarcutLinked(outerNode, triangles, dim, minX, minY, invSize, 0);
            return triangles;
        }

        static Node? LinkedList(double[] data, int start, int end, int dim, bool clockwise)
        {
            Node? last = null;
            if (clockwise == (SignedArea(data, start, end, dim) > 0))
            {
                for (int i = start; i < end; i += dim) last = InsertNode(i / dim, data[i], data[i + 1], last);
            }
            else
            {
                for (int i = end - dim; i >= start; i -= dim) last = InsertNode(i / dim, data[i], data[i + 1], last);
            }
            if (last != null && Equals(last, last.next))
            {
                RemoveNode(last);
                last = last.next;
            }
            return last;
        }

        static Node? FilterPoints(Node? start, Node? end = null)
        {
            if (start == null) return start;
            end ??= start;
            Node p = start;
            bool again;
            do
            {
                again = false;
                if (!p.steiner && (Equals(p, p.next) || Area(p.prev, p, p.next) == 0))
                {
                    RemoveNode(p);
                    p = end = p.prev;
                    if (p == p.next) break;
                    again = true;
                }
                else p = p.next;
            } while (again || p != end);
            return end;
        }

        static void EarcutLinked(Node? ear, List<int> triangles, int dim, double minX, double minY, double invSize, int pass)
        {
            if (ear == null) return;
            if (pass == 0 && invSize != 0) IndexCurve(ear, minX, minY, invSize);

            Node stop = ear;
            while (ear!.prev != ear.next)
            {
                Node prev = ear.prev, next = ear.next;
                if (invSize != 0 ? IsEarHashed(ear, minX, minY, invSize) : IsEar(ear))
                {
                    triangles.Add(prev.i); triangles.Add(ear.i); triangles.Add(next.i);
                    RemoveNode(ear);
                    ear = next.next;
                    stop = next.next;
                    continue;
                }
                ear = next;
                if (ear == stop)
                {
                    if (pass == 0) EarcutLinked(FilterPoints(ear), triangles, dim, minX, minY, invSize, 1);
                    else if (pass == 1)
                    {
                        ear = CureLocalIntersections(FilterPoints(ear)!, triangles);
                        EarcutLinked(ear, triangles, dim, minX, minY, invSize, 2);
                    }
                    else if (pass == 2) SplitEarcut(ear, triangles, dim, minX, minY, invSize);
                    break;
                }
            }
        }

        static bool IsEar(Node ear)
        {
            Node a = ear.prev, b = ear, c = ear.next;
            if (Area(a, b, c) >= 0) return false;
            double ax = a.x, bx = b.x, cx = c.x, ay = a.y, by = b.y, cy = c.y;
            double x0 = Math.Min(ax, Math.Min(bx, cx)), y0 = Math.Min(ay, Math.Min(by, cy)), x1 = Math.Max(ax, Math.Max(bx, cx)), y1 = Math.Max(ay, Math.Max(by, cy));
            Node p = c.next;
            while (p != a)
            {
                if (p.x >= x0 && p.x <= x1 && p.y >= y0 && p.y <= y1 &&
                    PointInTriangleExceptFirst(ax, ay, bx, by, cx, cy, p.x, p.y) && Area(p.prev, p, p.next) >= 0) return false;
                p = p.next;
            }
            return true;
        }

        static bool IsEarHashed(Node ear, double minX, double minY, double invSize)
        {
            Node a = ear.prev, b = ear, c = ear.next;
            if (Area(a, b, c) >= 0) return false;
            double ax = a.x, bx = b.x, cx = c.x, ay = a.y, by = b.y, cy = c.y;
            double x0 = Math.Min(ax, Math.Min(bx, cx)), y0 = Math.Min(ay, Math.Min(by, cy)), x1 = Math.Max(ax, Math.Max(bx, cx)), y1 = Math.Max(ay, Math.Max(by, cy));
            int minZ = ZOrder(x0, y0, minX, minY, invSize), maxZ = ZOrder(x1, y1, minX, minY, invSize);
            Node? p = ear.prevZ, n = ear.nextZ;
            while (p != null && p.z >= minZ && n != null && n.z <= maxZ)
            {
                if (p.x >= x0 && p.x <= x1 && p.y >= y0 && p.y <= y1 && p != a && p != c &&
                    PointInTriangleExceptFirst(ax, ay, bx, by, cx, cy, p.x, p.y) && Area(p.prev, p, p.next) >= 0) return false;
                p = p.prevZ;
                if (n.x >= x0 && n.x <= x1 && n.y >= y0 && n.y <= y1 && n != a && n != c &&
                    PointInTriangleExceptFirst(ax, ay, bx, by, cx, cy, n.x, n.y) && Area(n.prev, n, n.next) >= 0) return false;
                n = n.nextZ;
            }
            while (p != null && p.z >= minZ)
            {
                if (p.x >= x0 && p.x <= x1 && p.y >= y0 && p.y <= y1 && p != a && p != c &&
                    PointInTriangleExceptFirst(ax, ay, bx, by, cx, cy, p.x, p.y) && Area(p.prev, p, p.next) >= 0) return false;
                p = p.prevZ;
            }
            while (n != null && n.z <= maxZ)
            {
                if (n.x >= x0 && n.x <= x1 && n.y >= y0 && n.y <= y1 && n != a && n != c &&
                    PointInTriangleExceptFirst(ax, ay, bx, by, cx, cy, n.x, n.y) && Area(n.prev, n, n.next) >= 0) return false;
                n = n.nextZ;
            }
            return true;
        }

        static Node? CureLocalIntersections(Node start, List<int> triangles)
        {
            Node p = start;
            do
            {
                Node a = p.prev, b = p.next.next;
                if (!Equals(a, b) && Intersects(a, p, p.next, b) && LocallyInside(a, b) && LocallyInside(b, a))
                {
                    triangles.Add(a.i); triangles.Add(p.i); triangles.Add(b.i);
                    RemoveNode(p);
                    RemoveNode(p.next);
                    p = start = b;
                }
                p = p.next;
            } while (p != start);
            return FilterPoints(p);
        }

        static void SplitEarcut(Node start, List<int> triangles, int dim, double minX, double minY, double invSize)
        {
            Node a = start;
            do
            {
                Node b = a.next.next;
                while (b != a.prev)
                {
                    if (a.i != b.i && IsValidDiagonal(a, b))
                    {
                        Node c = SplitPolygon(a, b);
                        a = FilterPoints(a, a.next)!;
                        c = FilterPoints(c, c.next)!;
                        EarcutLinked(a, triangles, dim, minX, minY, invSize, 0);
                        EarcutLinked(c, triangles, dim, minX, minY, invSize, 0);
                        return;
                    }
                    b = b.next;
                }
                a = a.next;
            } while (a != start);
        }

        static Node EliminateHoles(double[] data, int[] holeIndices, Node outerNode, int dim)
        {
            var queue = new List<Node>();
            for (int i = 0, len = holeIndices.Length; i < len; i++)
            {
                int start = holeIndices[i] * dim;
                int end = i < len - 1 ? holeIndices[i + 1] * dim : data.Length;
                var list = LinkedList(data, start, end, dim, false);
                if (list == null) continue;
                if (list == list.next) list.steiner = true;
                queue.Add(GetLeftmost(list));
            }
            queue.Sort(CompareXYSlope);
            for (int i = 0; i < queue.Count; i++) outerNode = EliminateHole(queue[i], outerNode);
            return outerNode;
        }

        static int CompareXYSlope(Node a, Node b)
        {
            double result = a.x - b.x;
            if (result == 0)
            {
                result = a.y - b.y;
                if (result == 0)
                {
                    double aSlope = (a.next.y - a.y) / (a.next.x - a.x);
                    double bSlope = (b.next.y - b.y) / (b.next.x - b.x);
                    result = aSlope - bSlope;
                }
            }
            return result < 0 ? -1 : result > 0 ? 1 : 0;
        }

        static Node EliminateHole(Node hole, Node outerNode)
        {
            var bridge = FindHoleBridge(hole, outerNode);
            if (bridge == null) return outerNode;
            var bridgeReverse = SplitPolygon(bridge, hole);
            FilterPoints(bridgeReverse, bridgeReverse.next);
            return FilterPoints(bridge, bridge.next)!;
        }

        static Node? FindHoleBridge(Node hole, Node outerNode)
        {
            Node p = outerNode;
            double hx = hole.x, hy = hole.y, qx = double.NegativeInfinity;
            Node? m = null;
            if (Equals(hole, p)) return p;
            do
            {
                if (Equals(hole, p.next)) return p.next;
                if (hy <= p.y && hy >= p.next.y && p.next.y != p.y)
                {
                    double x = p.x + (hy - p.y) * (p.next.x - p.x) / (p.next.y - p.y);
                    if (x <= hx && x > qx)
                    {
                        qx = x;
                        m = p.x < p.next.x ? p : p.next;
                        if (x == hx) return m;
                    }
                }
                p = p.next;
            } while (p != outerNode);

            if (m == null) return null;

            Node stop = m;
            double mx = m.x, my = m.y, tanMin = double.PositiveInfinity;
            p = m;
            do
            {
                if (hx >= p.x && p.x >= mx && hx != p.x &&
                    PointInTriangle(hy < my ? hx : qx, hy, mx, my, hy < my ? qx : hx, hy, p.x, p.y))
                {
                    double tan = Math.Abs(hy - p.y) / (hx - p.x);
                    if (LocallyInside(p, hole) && (tan < tanMin || (tan == tanMin && (p.x > m.x || (p.x == m.x && SectorContainsSector(m, p))))))
                    {
                        m = p;
                        tanMin = tan;
                    }
                }
                p = p.next;
            } while (p != stop);
            return m;
        }

        static bool SectorContainsSector(Node m, Node p) => Area(m.prev, m, p.prev) < 0 && Area(p.next, m, m.next) < 0;

        static void IndexCurve(Node start, double minX, double minY, double invSize)
        {
            Node p = start;
            do
            {
                if (p.z == 0) p.z = ZOrder(p.x, p.y, minX, minY, invSize);
                p.prevZ = p.prev;
                p.nextZ = p.next;
                p = p.next;
            } while (p != start);
            p.prevZ!.nextZ = null;
            p.prevZ = null;
            SortLinked(p);
        }

        static Node? SortLinked(Node? list)
        {
            int numMerges, inSize = 1;
            do
            {
                Node? p = list, tail = null;
                list = null;
                numMerges = 0;
                while (p != null)
                {
                    numMerges++;
                    Node? q = p;
                    int pSize = 0;
                    for (int i = 0; i < inSize; i++)
                    {
                        pSize++;
                        q = q!.nextZ;
                        if (q == null) break;
                    }
                    int qSize = inSize;
                    while (pSize > 0 || (qSize > 0 && q != null))
                    {
                        Node e;
                        if (pSize != 0 && (qSize == 0 || q == null || p!.z <= q.z))
                        {
                            e = p!; p = p!.nextZ; pSize--;
                        }
                        else
                        {
                            e = q!; q = q!.nextZ; qSize--;
                        }
                        if (tail != null) tail.nextZ = e; else list = e;
                        e.prevZ = tail;
                        tail = e;
                    }
                    p = q;
                }
                tail!.nextZ = null;
                inSize *= 2;
            } while (numMerges > 1);
            return list;
        }

        static int ZOrder(double xd, double yd, double minX, double minY, double invSize)
        {
            int x = (int)((xd - minX) * invSize);
            int y = (int)((yd - minY) * invSize);
            x = (x | (x << 8)) & 0x00FF00FF;
            x = (x | (x << 4)) & 0x0F0F0F0F;
            x = (x | (x << 2)) & 0x33333333;
            x = (x | (x << 1)) & 0x55555555;
            y = (y | (y << 8)) & 0x00FF00FF;
            y = (y | (y << 4)) & 0x0F0F0F0F;
            y = (y | (y << 2)) & 0x33333333;
            y = (y | (y << 1)) & 0x55555555;
            return x | (y << 1);
        }

        static Node GetLeftmost(Node start)
        {
            Node p = start, leftmost = start;
            do
            {
                if (p.x < leftmost.x || (p.x == leftmost.x && p.y < leftmost.y)) leftmost = p;
                p = p.next;
            } while (p != start);
            return leftmost;
        }

        static bool PointInTriangle(double ax, double ay, double bx, double by, double cx, double cy, double px, double py) =>
            (cx - px) * (ay - py) >= (ax - px) * (cy - py) &&
            (ax - px) * (by - py) >= (bx - px) * (ay - py) &&
            (bx - px) * (cy - py) >= (cx - px) * (by - py);

        static bool PointInTriangleExceptFirst(double ax, double ay, double bx, double by, double cx, double cy, double px, double py) =>
            !(ax == px && ay == py) && PointInTriangle(ax, ay, bx, by, cx, cy, px, py);

        static bool IsValidDiagonal(Node a, Node b) =>
            a.next.i != b.i && a.prev.i != b.i && !IntersectsPolygon(a, b) &&
            (LocallyInside(a, b) && LocallyInside(b, a) && MiddleInside(a, b) &&
             (Area(a.prev, a, b.prev) != 0 || Area(a, b.prev, b) != 0) ||
             Equals(a, b) && Area(a.prev, a, a.next) > 0 && Area(b.prev, b, b.next) > 0);

        static double Area(Node p, Node q, Node r) => (q.y - p.y) * (r.x - q.x) - (q.x - p.x) * (r.y - q.y);

        static bool Equals(Node p1, Node p2) => p1.x == p2.x && p1.y == p2.y;

        static bool Intersects(Node p1, Node q1, Node p2, Node q2)
        {
            int o1 = Sign(Area(p1, q1, p2)), o2 = Sign(Area(p1, q1, q2)), o3 = Sign(Area(p2, q2, p1)), o4 = Sign(Area(p2, q2, q1));
            if (o1 != o2 && o3 != o4) return true;
            if (o1 == 0 && OnSegment(p1, p2, q1)) return true;
            if (o2 == 0 && OnSegment(p1, q2, q1)) return true;
            if (o3 == 0 && OnSegment(p2, p1, q2)) return true;
            if (o4 == 0 && OnSegment(p2, q1, q2)) return true;
            return false;
        }

        static bool OnSegment(Node p, Node q, Node r) =>
            q.x <= Math.Max(p.x, r.x) && q.x >= Math.Min(p.x, r.x) && q.y <= Math.Max(p.y, r.y) && q.y >= Math.Min(p.y, r.y);

        static int Sign(double num) => num > 0 ? 1 : num < 0 ? -1 : 0;

        static bool IntersectsPolygon(Node a, Node b)
        {
            Node p = a;
            do
            {
                if (p.i != a.i && p.next.i != a.i && p.i != b.i && p.next.i != b.i && Intersects(p, p.next, a, b)) return true;
                p = p.next;
            } while (p != a);
            return false;
        }

        static bool LocallyInside(Node a, Node b) =>
            Area(a.prev, a, a.next) < 0
                ? Area(a, b, a.next) >= 0 && Area(a, a.prev, b) >= 0
                : Area(a, b, a.prev) < 0 || Area(a, a.next, b) < 0;

        static bool MiddleInside(Node a, Node b)
        {
            Node p = a;
            bool inside = false;
            double px = (a.x + b.x) / 2, py = (a.y + b.y) / 2;
            do
            {
                if (((p.y > py) != (p.next.y > py)) && p.next.y != p.y && (px < (p.next.x - p.x) * (py - p.y) / (p.next.y - p.y) + p.x))
                    inside = !inside;
                p = p.next;
            } while (p != a);
            return inside;
        }

        static Node SplitPolygon(Node a, Node b)
        {
            var a2 = new Node(a.i, a.x, a.y);
            var b2 = new Node(b.i, b.x, b.y);
            Node an = a.next, bp = b.prev;
            a.next = b; b.prev = a;
            a2.next = an; an.prev = a2;
            b2.next = a2; a2.prev = b2;
            bp.next = b2; b2.prev = bp;
            return b2;
        }

        static Node InsertNode(int i, double x, double y, Node? last)
        {
            var p = new Node(i, x, y);
            if (last == null)
            {
                p.prev = p; p.next = p;
            }
            else
            {
                p.next = last.next; p.prev = last;
                last.next.prev = p; last.next = p;
            }
            return p;
        }

        static void RemoveNode(Node p)
        {
            p.next.prev = p.prev;
            p.prev.next = p.next;
            if (p.prevZ != null) p.prevZ.nextZ = p.nextZ;
            if (p.nextZ != null) p.nextZ.prevZ = p.prevZ;
        }

        static double SignedArea(double[] data, int start, int end, int dim)
        {
            double sum = 0;
            for (int i = start, j = end - dim; i < end; i += dim)
            {
                sum += (data[j] - data[i]) * (data[i + 1] + data[j + 1]);
                j = i;
            }
            return sum;
        }
    }
}
