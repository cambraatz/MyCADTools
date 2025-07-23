using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.EditorInput;
using System.Windows.Shapes;
using System.Security.Cryptography.Pkcs;

namespace MyPlantingTool
{
    public class CirclePacker
    {
        private List<Point2d> _boundaryPolygon;
        private List<PlantCircle> _packedCircles;
        private List<double> _plantRadii;
        private Tolerance _tolerance;
        private Editor Editor;
        private bool _verbose;

        public CirclePacker(List<Point2d> boundaryPolygon, List<double> plantRadii, Tolerance tolerance, Editor editor, bool verbose = false)
        {
            _boundaryPolygon = boundaryPolygon;
            _packedCircles = new List<PlantCircle>();
            _plantRadii = plantRadii;
            _tolerance = tolerance;
            Editor = editor;
            _verbose = verbose;
        }
        public List<PlantCircle> PackCircles()
        {
            // initial placement, start with placing in center of polygon
            Point2d centroid = GetPolygonCentroid(_boundaryPolygon);

            foreach(double radius in _plantRadii)
            {
                if (CanPlaceCircle(centroid, radius, _packedCircles, _boundaryPolygon, _tolerance))
                {
                    _packedCircles.Add(new PlantCircle(centroid, radius));
                    // LogToScreen(_editor, $"\nPlaced initial circle at centroid: {centroid.X:F2},{centroid.Y:F2} R={radius:F2}", _verbose);
                    break; // Placed one central circle, now grow from there
                }
            }

            // if no circle could be placed at centroid, try to find starting point
            if (_packedCircles.Count == 0 && _boundaryPolygon.Count > 0)
            {
                foreach (double radius in _plantRadii)
                {
                    if (CanPlaceCircle(_boundaryPolygon[0], radius, _packedCircles, _boundaryPolygon, _tolerance))
                    {
                        _packedCircles.Add(new PlantCircle(_boundaryPolygon[0], radius));
                        // LogToScreen(_editor, $"\nPlaced initial circle at first boundary point: {_boundaryPolygon[0].X:F2},{_boundaryPolygon[0].Y:F2} R={radius:F2}", _verbose);
                        break;
                    }
                }
            }

            // main packing loop (iterative growth)
            // more advanced approach would use...
            // 1) Finding points along the 'Voronoi' diagram of existing circles and boundary.
            // 2) Using a discrete grid to find empty spots.
            int maxIterations = 5000;
            int placedCount = 0;

            // continuously place circles, prioritizing larger ones, at candidate points generated from existing circles
            for (int k = 0; k < maxIterations; k++)
            {
                bool placedCircle = false;
                List<Point2d> candidatePoints = GenerateCandidatePoints(_packedCircles, _boundaryPolygon, _plantRadii.Max() * 2);
                candidatePoints = candidatePoints.OrderBy(p => p.GetDistanceTo(centroid)).ToList();

                foreach (double radius in _plantRadii)
                {
                    Point2d? bestCandidate = null;
                    // find best candidate point for this radius
                    foreach (Point2d candidate in candidatePoints)
                    {
                        if (CanPlaceCircle(candidate, radius, _packedCircles, _boundaryPolygon, _tolerance))
                        {
                            bestCandidate = candidate;
                            break;
                        }
                    }

                    if (bestCandidate.HasValue)
                    {
                        _packedCircles.Add(new PlantCircle(bestCandidate.Value, radius));
                        placedCircle = true;
                        placedCount++;
                        // LogToScreen(_editor, $"\nPlaced circle at {bestCandidate.Value.X:F2},{bestCandidate.Value.Y:F2} R={radius:F2}", _verbose);
                        // Regenerate candidates for next placement or at least re-evaluate.
                        // For simplicity, we break and let the main loop generate new candidates.
                        break; // Move to next iteration of outer loop
                    }
                }

                if (!placedCircle && k > 0 && _packedCircles.Count > 0)
                {
                    // If no circle was placed in this iteration, and we have some circles,
                    // we might be stuck or out of space.
                    break;
                }
                else if (!placedCircle && _packedCircles.Count == 0 && k > 0)
                {
                    // No circles placed at all, and no more good candidates.

                    // WE NEED TO FIGURE OUT THE PLOTTING HERE!!!
                    /* LogToScreen(_editor, "\nCould not place any circles. Boundary might be too small or inaccessible.", _verbose); */
                    break;
                }
            }

            //LogToScreen(_editor, $"\nFinished packing. Total circles placed: {placedCount}", _verbose);
            return _packedCircles;
        }

        // find center of a polygon
        private Point2d GetPolygonCentroid(List<Point2d> polygon)
        {
            if (polygon == null || polygon.Count == 0) { return Point2d.Origin; }

            double sumX = 0, sumY = 0;
            foreach(Point2d p in polygon)
            {
                sumX += p.X;
                sumY += p.Y;
            }
            return new Point2d(sumX / polygon.Count, sumY / polygon.Count);
        }

        // validate if a circle can be placed at a given point
        private bool CanPlaceCircle(Point2d center, double radius, List<PlantCircle> packedCircles, List<Point2d> boundaryPolygon, Tolerance tolerance)
        {
            // ensure circle is within boundary polygon
            double signedDistToBoundary = GeometryService.SignedDistanceToPolygonBoundary(center, boundaryPolygon, tolerance);
            if (signedDistToBoundary + tolerance.EqualPoint < radius)
            {
                return false; // circle is too large to fit within the boundary
            }

            // ensure circle does not overlap with existing packed circles
            foreach (PlantCircle circle in packedCircles)
            {
                PlantCircle candidate = new PlantCircle(center, radius);
                if (candidate.Overlaps(circle, tolerance))
                {
                    return false;
                }
            }
            return true;
        }

        private List<Point2d> GenerateCandidatePoints(List<PlantCircle> existingCircles, List<Point2d> boundary, double spacingHint)
        {
            List<Point2d> candidates = new List<Point2d>();
            if (existingCircles.Count == 0)
            {
                candidates.Add(boundary[0]); // start with first boundary point if no circles exist
                if (boundary.Count > 0)
                {
                    candidates.Add(boundary[0]); // first point of boundary
                    for (int i = 0; i < boundary.Count; i += Math.Max(1, boundary.Count / 10))
                    {
                        candidates.Add(boundary[i]);
                    }
                }
                return candidates;
            }

            // generate candidate points around existing circles
            foreach(PlantCircle circle in existingCircles)
            {
                // Try points at various angles around the existing circle, just outside its radius
                // You can adjust the number of points to check
                int numPointsAroundCircle = 12;
                for (int i = 0; i < numPointsAroundCircle; i++)
                { 
                    double angle = (2 * Math.PI / numPointsAroundCircle) * i;
                    // test distance just outside the circle's radius
                    double testRadius = circle.Radius + _plantRadii.Min() + _tolerance.EqualPoint * 2;
                    Point2d candidate = new Point2d(
                        circle.Center.X + testRadius * Math.Cos(angle),
                        circle.Center.Y + testRadius * Math.Sin(angle)
                    );
                    candidates.Add(candidate);
                }
            }

            return candidates.Where(p => GeometryService.IsPointInsidePolygon(p, boundary, _tolerance)).Distinct().ToList();
        }

        // --- Specific Requirements Implementation ---

        // This will be more complex and might involve re-ordering or re-prioritizing placements.
        // Let's add methods that will be called *within* or *after* the basic packing loop.

        public void ApplyClusteringAndCentralPlacement(List<PlantCircle> packedCircles)
        {
            // This is where the custom logic gets applied.
            // This can be done post-processing, or integrated into the packing loop.
            // For a first pass, let's consider post-processing or a more controlled packing.

            // Requirement 1: Group circles in clusters of odd numbers.
            // This is very challenging with a purely geometric packing algorithm.
            // It often implies a "layout" stage separate from pure packing.
            // Possible approaches:
            // a) Pack without this constraint, then try to nudge/swap to form clusters. (Very hard)
            // b) Define "cluster centers" first, then pack circles around them. (More feasible)
            // c) Prioritize placing circles such that they naturally form groups.

            // For now, let's implement this as a reporting/validation aspect,
            // or a highly controlled packing process. If we are just placing circles
            // as points, we can't easily force "odd clusters" without defining
            // what constitutes a cluster and iteratively placing.

            // Requirement 2: Place large symbols near to the center.
            // This can be integrated by prioritizing placement of larger radii closer to the centroid
            // in the `PackCircles` method. This is why we ordered `_plantRadii` descending
            // and sorted `candidatePoints` by distance to centroid.
            // The initial placement already attempts this.

            // More advanced:
            // You might need a scoring function for candidate points that considers:
            // - Distance to polygon boundary
            // - Distance to centroid (for large circles)
            // - Proximity to existing circles (for packing)
            // - 'Odd' grouping could be defined by trying to make groups of 3, 5, 7, etc.
            //   This often requires a more intelligent candidate generation and selection.

            // For the first iteration, let's stick to placing larger symbols near the center
            // and acknowledge that "odd number clusters" is a much harder problem for a pure packing algo.
            // It might require a separate "clustering" step that assigns circles to conceptual groups.
            // Example for odd clustering (high-level, not directly code):
            // 1. Create a set of "cluster centers" within the polygon.
            // 2. For each cluster center, try to pack 3, 5, or 7 circles around it,
            //    respecting the main boundary and other cluster boundaries.
            // 3. Fill remaining space with individual circles.
        }
    }
}
