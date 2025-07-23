using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
//using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

namespace MyPlantingTool
{
    public static class GeometryService
    {
        private static int _approxCircleSegments = 64;
        private static int _approxArcSegments = _approxCircleSegments / 4;
        private static int _approxSplineEllipseSegments = 48;

        public static Tolerance _defaultTolerance = new Tolerance(1e-9, 1e-9);

        // HELPER: conditionally log debug messages...
        private static void LogToScreen(Editor ed, string message, bool verbose)
        {
            if (verbose)
            {
                ed.WriteMessage(message);
            }
        }

        // HELPER: checks if a List<Point2d> represents a closed polylin...
        private static bool IsPolylineClosed(List<Point2d> polylinePoints)
        {
            // A closed polyline must have at least 2 points (segment) and its first point
            // must be equal to its last point within a tolerance.
            // For a single point, it cannot form a closed loop.
            if (polylinePoints == null || polylinePoints.Count < 2)
            {
                return false;
            }

            // Compare the first point to the last point
            return polylinePoints[0].IsEqualTo(polylinePoints[polylinePoints.Count - 1], _defaultTolerance);
        }
        public static List<Point2d>? GetBoundaryPoints(DBObject acadObject, Transaction tr, Editor ed, bool verbose)
        {
            //Editor ed = Application.DocumentManager.MdiActiveDocument.Editor;

            List<Point2d> boundary = new List<Point2d>();

            if (acadObject is Polyline polyline)
            {
                if (polyline.Closed)
                {
                    for (int i = 0; i < polyline.NumberOfVertices; i++)
                    {
                        boundary.Add(polyline.GetPoint2dAt(i));
                    }
                    return boundary;
                }
                else
                {
                    //ed.WriteMessage("\nPolyline is not closed.");
                    LogToScreen(ed, "\nPolyline is not closed.", verbose);
                    return null;
                }
            }
            else if (acadObject is Polyline3d polyline3d)
            {
                // Convert Polyline3d to 2D points (ignoring Z for now, assuming planar bed)...
                if (polyline3d.Closed)
                {
                    // Iterate through vertices
                    foreach (ObjectId vertId in polyline3d)
                    {
                        using (PolylineVertex3d vert = tr.GetObject(vertId, OpenMode.ForRead) as PolylineVertex3d)
                        {
                            if (vert != null)
                            {
                                boundary.Add(new Point2d(vert.Position.X, vert.Position.Y));
                            }
                        }
                    }
                    // Ensure the first point is added again if it's closed to form a proper loop for algorithms
                    if (boundary.Count > 0 && !boundary[0].Equals(boundary[boundary.Count - 1]))
                    {
                        boundary.Add(boundary[0]); // Add closing point if needed for polygon algos
                    }
                    return boundary;
                }
                else
                {
                    //ed.WriteMessage("\nPolyline3d is not closed.");
                    LogToScreen(ed, "\nPolyline3d is not closed.", verbose);
                    return null;
                }
            }
            else if (acadObject is Polyline2d polyline2d)
            {
                if (polyline2d.Closed)
                {
                    // Iterate through vertices
                    foreach (ObjectId vertId in polyline2d)
                    {
                        using (Vertex2d vert = tr.GetObject(vertId, OpenMode.ForRead) as Vertex2d)
                        {
                            if (vert != null)
                            {
                                boundary.Add(new Point2d(vert.Position.X, vert.Position.Y));
                            }
                        }
                    }
                    // Ensure the first point is added again if it's closed to form a proper loop for algorithms
                    if (boundary.Count > 0 && !boundary[0].Equals(boundary[boundary.Count - 1]))
                    {
                        boundary.Add(boundary[0]); // Add closing point if needed for polygon algos
                    }
                    return boundary;
                }
                else
                {
                    //ed.WriteMessage("\nPolyline2d is not closed.");
                    LogToScreen(ed, "\nPolyline2d is not closed.", verbose);
                    return null;
                }
            }
            else if (acadObject is Circle circle)
            {
                // approximate circle with segments, predefined above...
                for (int i = 0; i < _approxCircleSegments; i++)
                {
                    double angle = (2 * System.Math.PI / _approxCircleSegments) * i;
                    double x = circle.Center.X + circle.Radius * System.Math.Cos(angle);
                    double y = circle.Center.Y + circle.Radius * System.Math.Sin(angle);
                    boundary.Add(new Point2d(x, y));
                }
                // Ensure the loop is explicitly closed
                if (boundary.Count > 0 && !boundary[0].IsEqualTo(boundary[boundary.Count - 1], new Tolerance(1e-9, 1e-9)))
                {
                    boundary.Add(boundary[0]);
                }
                return boundary;
            }
            else if (acadObject is Hatch hatch)
            {
                hatch.EvaluateHatch(true);

                // assume we want the first external or default loop...
                List<Point2d> currLoopPoints = new List<Point2d>();
                bool foundValidLoop = false;

                for (int i = 0; i < hatch.NumberOfLoops; i++)
                {
                    HatchLoop loop = hatch.GetLoopAt(i);

                    // primarily interested in outermost boundary...
                    if (loop.LoopType == HatchLoopTypes.External || loop.LoopType == HatchLoopTypes.Default)
                    {
                        currLoopPoints.Clear(); // clear points for curr loop before processing...
                        try
                        {
                            foreach (Curve2d curve2d in loop.Curves)
                            {
                                // add start point of curr segment if its the first point of loop...
                                // OR if it is not a duplicate of the last point added...
                                if (currLoopPoints.Count == 0 || !curve2d.StartPoint.IsEqualTo(currLoopPoints.Last(), _defaultTolerance))
                                {
                                    currLoopPoints.Add(curve2d.StartPoint);
                                }
                                if (curve2d is LineSegment2d lineSegment)
                                {
                                    // add start point, adds each subsequent loop assuming they one ends where the next starts (ie: implicit order)...
                                    // avoid adding duplicates of the last point, especially crucial at segment junctions...
                                    if (boundary.Count == 0 || !lineSegment.StartPoint.IsEqualTo(boundary[boundary.Count - 1], _defaultTolerance))
                                    {
                                        boundary.Add(lineSegment.StartPoint);
                                    }
                                }
                                else if (curve2d is CircularArc2d arcSegment)
                                {
                                    double startAngle = arcSegment.StartAngle;
                                    double endAngle = arcSegment.EndAngle;

                                    // handle angles spanning 0/2pi correctly for iteration...
                                    if (endAngle < startAngle)
                                    {
                                        endAngle += (2 * System.Math.PI);
                                    }

                                    for (int j = 0; j <= _approxArcSegments; j++)
                                    {
                                        double angle = startAngle + (endAngle - startAngle) * ((double)j / _approxArcSegments);
                                        Point2d pointOnArc = arcSegment.EvaluatePoint(angle);
                                        currLoopPoints.Add(pointOnArc);
                                    }
                                }
                                else if (curve2d is EllipticalArc2d ellipticalArc)
                                {
                                    LogToScreen(ed, $"\nApproximating EllipticalArc2d by segmenting...", verbose);
                                    double startAngle = ellipticalArc.StartAngle;
                                    double endAngle = ellipticalArc.EndAngle;

                                    // handle cases of end angle < start angle (ie: crossing 0/2PI boundary...
                                    // adjust endAngle to ensure positive direction iteration...
                                    if (endAngle < startAngle)
                                    {
                                        endAngle += (2 * System.Math.PI);
                                    }

                                    // EvaluatePoint method for EllipticalArc2d takes angle in radians...
                                    // relative to the ellipse's coordinate system...
                                    for (int j = 1; j <= _approxSplineEllipseSegments; j++)
                                    {
                                        double currAngle = startAngle + (endAngle - startAngle) * ((double)j / _approxSplineEllipseSegments);
                                        Point2d pointOnEllipse = ellipticalArc.EvaluatePoint(currAngle);
                                        currLoopPoints.Add(pointOnEllipse);
                                    }
                                }
                                else if (curve2d is NurbCurve2d nurbCurve)
                                {
                                    LogToScreen(ed, $"\nApproximating NurbCurve2d (Spline) by segmenting...", verbose);
                                    double startParam = nurbCurve.StartParameter;
                                    double endParam = nurbCurve.EndParameter;

                                    for (int j = 1; j <= _approxSplineEllipseSegments; j++)
                                    {
                                        double param = startParam + (endParam - startParam) * ((double)j / _approxSplineEllipseSegments);
                                        Point2d pointOnSpline = nurbCurve.EvaluatePoint(param);
                                        currLoopPoints.Add(pointOnSpline);
                                    }
                                }
                                else
                                {
                                    // add more curve types (ie: Elliptical Arc 2d, Spline 2d, etc.) here...
                                    // each new type will require its own approximation technique...
                                    //ed.WriteMessage($"\nUnsupported curve type in hatch boundary: {curve2d.GetType().Name}. Cannot approximate.");
                                    LogToScreen(ed, $"\nWarning: Skipping detailed approximation for unsupported curve type in hatch boundary: {curve2d.GetType().Name}. Adding end point only.", verbose);
                                    // The start point should already be handled by the general continuity check before the if-else if chain.
                                    if (!curve2d.EndPoint.IsEqualTo(currLoopPoints.Last(), _defaultTolerance))
                                    {
                                        currLoopPoints.Add(curve2d.EndPoint);
                                    }
                                }
                            }

                            // after processing curves in loop, ensure boundary is closed...
                            // ensure first + last points are the same, if not add the first to end of loop...
                            if (currLoopPoints.Count > 0 && !currLoopPoints[0].IsEqualTo(currLoopPoints.Last(), _defaultTolerance))
                            {
                                currLoopPoints.Add(currLoopPoints[0]);
                            }

                            // ensure valid polygon (ie: 3 distinct points)...
                            if (currLoopPoints.Count >= 3)
                            {
                                foundValidLoop = true;
                                boundary = currLoopPoints;
                                break;
                            }
                            else
                            {
                                LogToScreen(ed, "\nExtracted hatch loop resulted in too few distinct points to form a valid boundary. Trying next loop if available.", verbose);
                            }

                            return boundary;
                        }
                        catch (System.Exception ex)
                        {
                            //ed.WriteMessage($"Error extracting complex hatch boundary from curves: {ex.Message}");
                            LogToScreen(ed, $"\nError extracting complex hatch boundary from curves: {ex.Message}", verbose);
                            return null;
                        }
                    }
                }

                if (foundValidLoop)
                {
                    return boundary;
                }
                else
                {
                    // failed to isolate outer boundary to loop...
                    // ed.WriteMessage("\nHatch has no accessible outer boundary loop or extraction failed.");
                    LogToScreen(ed, "\nHatch has no accessible outer boundary loop or extraction failed.", verbose);
                    return null;
                }
                    
            }
            else
            {
                // aCadObject is not a recognizeable boundary type...
                //ed.WriteMessage("\nSelected object is not a recognized or extractable boundary type.");
                LogToScreen(ed, "\nSelected object is not a recognized or extractable boundary type.", verbose);
                return null;
            }   
        }

        public static bool IsPointInsidePolygon(Point2d point, List<Point2d> polygon, Tolerance? tolerance)
        {
            if (polygon == null || polygon.Count < 3)
            {
                return false; // Not a valid polygon
            }
            if (tolerance == null)
            {
                tolerance = _defaultTolerance;
            }
            // Use the ray-casting algorithm to determine if the point is inside the polygon
            bool inside = false;
            int n = polygon.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Point2d pi = polygon[i];
                Point2d pj = polygon[j];
                // Check if the point is on the edge of the polygon
                if (pi.IsEqualTo(point, tolerance.Value) || pj.IsEqualTo(point, tolerance.Value))
                {
                    return true; // Point is on the boundary
                }
                // Check if the point is within the y-range of the edge
                if ((pi.Y > point.Y) != (pj.Y > point.Y))
                {
                    // Calculate intersection with the edge
                    double slope = (pj.X - pi.X) / (pj.Y - pi.Y);
                    double xIntersect = pi.X + slope * (point.Y - pi.Y);
                    if (xIntersect == point.X)
                    {
                        return true; // Point is on the boundary
                    }
                    if (xIntersect > point.X)
                    {
                        inside = !inside; // Toggle inside status
                    }
                }
            }
            return inside;
        }

        public static double SignedDistanceToPolygonBoundary(Point2d point, List<Point2d> polygon, Tolerance? tolerance)
        {
            if (polygon == null || polygon.Count < 2)
            {
                return double.PositiveInfinity; // Not a valid polygon
            }

            double minDistance = double.PositiveInfinity;
            int n = polygon.Count;

            // Determine if the point is inside the polygon with signed distance
            for (int i=0; i<n; i++)
            {
                Point2d p1 = polygon[i];
                Point2d p2 = polygon[(i + 1) % n]; // Wrap around to the first point
                LineSegment2d segment = new LineSegment2d(p1, p2);

                // Calculate the distance from the point to the line segment p1-p2
                double distance = segment.GetDistanceTo(point);
                minDistance = Math.Min(minDistance, distance);
            }

            if (IsPointInsidePolygon(point, polygon, tolerance))
            {
                return -minDistance; // Inside the polygon, return negative distance
            }
            else
            {
                return minDistance; // Outside the polygon, return positive distance
            }
        }
    }
}
