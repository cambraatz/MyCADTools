using Autodesk.AutoCAD.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MyPlantingTool
{
    public class PlantCircle
    {
        public Point2d Center { get; set; }
        public double Radius { get; set; }

        public PlantCircle(Point2d center, double radius)
        {
            Center = center;
            Radius = radius;
        }

        // check for overlap with another circle
        public bool Overlaps(PlantCircle other, Tolerance tolerance)
        {
            double distance = Center.GetDistanceTo(other.Center);
            return distance < (Radius + other.Radius - tolerance.EqualPoint);
        }
    }
}
